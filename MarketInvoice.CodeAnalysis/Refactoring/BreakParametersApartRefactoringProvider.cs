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
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(BreakParametersApartRefactoringProvider)), Shared]
    internal class BreakParametersApartRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            SyntaxNode root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            CSharpSyntaxNode node = root.FindNode(context.Span) as CSharpSyntaxNode;
            if (node == null)
                return;

            ParameterListSyntax parameterListNode = GetParameterListNode(node);
            if (parameterListNode == null || parameterListNode.Parameters.Count < 2)
                return;

            CodeAction action;
            if (IsAlreadySplitOnMoreThanOneLines(parameterListNode))
                action = CodeAction.Create("Line-up parameters", c => CollapseParameters(context.Document, parameterListNode, c));
            else
                action = CodeAction.Create("Break parameters apart", c => BreakParametersApart(context.Document, parameterListNode, c));

            context.RegisterRefactoring(action);
        }

        private static bool IsAlreadySplitOnMoreThanOneLines(ParameterListSyntax parameterListNode)
        {
            return parameterListNode.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.CommaToken))
                .Any(token => token.GetAllTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)));
        }

        private static ParameterListSyntax GetParameterListNode(CSharpSyntaxNode node)
        {
            MethodDeclarationSyntax methodDeclaration = node.Parent as MethodDeclarationSyntax;
            if (methodDeclaration != null)
                return methodDeclaration.ParameterList;

            ConstructorDeclarationSyntax ctorDeclaration = node.Parent as ConstructorDeclarationSyntax;
            if (ctorDeclaration != null)
                return ctorDeclaration.ParameterList;

            return node.FirstAncestorOrSelf<ParameterListSyntax>(parent => true);
        }


        private async Task<Document> CollapseParameters(Document document, ParameterListSyntax parameterListNode, CancellationToken cancellationToken)
        {
            ParameterListSyntax updatedParameterList = SyntaxFactory.ParameterList(parameterListNode.OpenParenToken, 
                SyntaxFactory.SeparatedList(parameterListNode.Parameters.Select(p => p.WithoutLeadingTrivia().WithoutTrailingTrivia()).ToList()), 
                parameterListNode.CloseParenToken);

            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(parameterListNode, updatedParameterList);
            
            return await Formatter.FormatAsync(document.WithSyntaxRoot(newRoot), updatedParameterList.FullSpan);
        }


        private async Task<Document> BreakParametersApart(Document document, ParameterListSyntax parameterListNode, CancellationToken cancellationToken)
        {
            if (parameterListNode.Parameters.Count < 2)
                return document;

            ParameterSyntax firstParameter = parameterListNode.Parameters.First();

            List<ParameterSyntax> updatedParameters = new List<ParameterSyntax>();
            updatedParameters.Add(firstParameter.WithoutLeadingTrivia().WithoutTrailingTrivia());

            foreach (ParameterSyntax parameter in parameterListNode.Parameters.Skip(1).ToList())
                updatedParameters.Add(parameter
                                        .WithoutTrailingTrivia()
                                        .WithLeadingTrivia(SyntaxFactory.EndOfLine("\r\n"), GetIndentTrivia(parameterListNode)));

            ParameterListSyntax updatedParameterList = SyntaxFactory.ParameterList(parameterListNode.OpenParenToken, 
                                                                                   SyntaxFactory.SeparatedList(updatedParameters), 
                                                                                   parameterListNode.CloseParenToken);

            SyntaxNode root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(parameterListNode, updatedParameterList);
            return document.WithSyntaxRoot(newRoot);
        }

        private static SyntaxTrivia GetIndentTrivia(ParameterListSyntax parameterListNode)
        {
            int column = parameterListNode.Parameters.First().GetLocation().GetLineSpan().StartLinePosition.Character;

            StringBuilder paddingBuilder = new StringBuilder();
            for (int i = 0; i < column; i++)
                paddingBuilder.Append(" ");

            return SyntaxFactory.Whitespace(paddingBuilder.ToString());
        }
    }
}