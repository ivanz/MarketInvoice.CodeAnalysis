using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace MarketInvoice.CodeAnalysis.Refactoring
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(BreakArgumentsApartRefactoringProvider)), Shared]
    internal class BreakArgumentsApartRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            CSharpSyntaxNode node = root.FindNode(context.Span) as CSharpSyntaxNode;
            if (node == null)
                return;

            ArgumentListSyntax parameterListNode = GetParameterListNode(node);
            if (parameterListNode == null || parameterListNode.Arguments.Count < 2)
                return;

            CodeAction action;
            if (IsAlreadySplitOnMoreThanOneLines(parameterListNode))
                action = CodeAction.Create("Line-up arguments", c => CollapseParameters(context.Document, parameterListNode, c));
            else
                action = CodeAction.Create("Break arguments apart", c => BreakParametersApart(context.Document, parameterListNode, c));

            context.RegisterRefactoring(action);
        }

        private static bool IsAlreadySplitOnMoreThanOneLines(ArgumentListSyntax parameterListNode)
        {
            return parameterListNode.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.CommaToken))
                .Any(token => token.GetAllTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)));
        }

        private static ArgumentListSyntax GetParameterListNode(CSharpSyntaxNode node)
        {
            InvocationExpressionSyntax invocationExpression = node.Parent as InvocationExpressionSyntax;
            if (invocationExpression != null)
                return invocationExpression.ArgumentList;

            return node.FirstAncestorOrSelf<ArgumentListSyntax>(parent => true);
        }


        private async Task<Document> CollapseParameters(Document document, ArgumentListSyntax argumentListNode, CancellationToken cancellationToken)
        {
            ArgumentListSyntax updatedParameterList = SyntaxFactory.ArgumentList(argumentListNode.OpenParenToken, 
                SyntaxFactory.SeparatedList(argumentListNode.Arguments
                    .Select(argument => argument.WithoutLeadingTrivia().WithoutTrailingTrivia()).ToList()), 
                argumentListNode.CloseParenToken);

            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(argumentListNode, updatedParameterList);
            
            return await Formatter.FormatAsync(document.WithSyntaxRoot(newRoot), updatedParameterList.FullSpan);
        }


        private async Task<Document> BreakParametersApart(Document document, ArgumentListSyntax argumentListNode, CancellationToken cancellationToken)
        {
            if (argumentListNode.Arguments.Count < 2)
                return document;

            ArgumentSyntax firstParameter = argumentListNode.Arguments.First();

            List<ArgumentSyntax> updatedParameters = new List<ArgumentSyntax>();
            updatedParameters.Add(firstParameter.WithoutLeadingTrivia().WithoutTrailingTrivia());

            foreach (ArgumentSyntax parameter in argumentListNode.Arguments.Skip(1).ToList())
                updatedParameters.Add(parameter
                                        .WithoutTrailingTrivia()
                                        .WithLeadingTrivia(SyntaxFactory.EndOfLine("\n"), GetIndentTrivia(argumentListNode)));

            ArgumentListSyntax updatedParameterList = SyntaxFactory.ArgumentList(argumentListNode.OpenParenToken, 
                                                                                   SyntaxFactory.SeparatedList(updatedParameters), 
                                                                                   argumentListNode.CloseParenToken);

            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(argumentListNode, updatedParameterList);
            return document.WithSyntaxRoot(newRoot);
        }

        private static SyntaxTrivia GetIndentTrivia(ArgumentListSyntax parameterListNode)
        {
            int column = parameterListNode.Arguments.First().GetLocation().GetLineSpan().StartLinePosition.Character;

            StringBuilder paddingBuilder = new StringBuilder();
            for (int i = 0; i < column; i++)
                paddingBuilder.Append(" ");

            return SyntaxFactory.Whitespace(paddingBuilder.ToString());
        }
    }
}