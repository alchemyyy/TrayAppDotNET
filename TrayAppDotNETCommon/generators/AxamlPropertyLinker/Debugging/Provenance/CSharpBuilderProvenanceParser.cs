using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal static class CSharpBuilderProvenanceParser
{
    private const string DebugUIProvenanceTypeName =
        "TrayAppDotNETCommon.UI.Debugging.DebugUIProvenance";

    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && InvocationName(invocation) == "RecordBuilder";

    public static CSharpBuilderBoundary? Parse(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        IMethodSymbol? boundaryMethod = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
            as IMethodSymbol;
        if (boundaryMethod == null
            || boundaryMethod.ContainingType.ToDisplayString() != DebugUIProvenanceTypeName
            || invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        SyntaxNode? scope = FindExecutableScope(invocation);
        if (scope == null) return null;

        ExpressionSyntax targetExpression = invocation.ArgumentList.Arguments[0].Expression;
        TargetIdentity? target = CreateTargetIdentity(
            targetExpression,
            context.SemanticModel,
            cancellationToken);
        if (target == null) return null;

        IMethodSymbol? containingMethod = context.SemanticModel.GetEnclosingSymbol(
            invocation.SpanStart,
            cancellationToken) as IMethodSymbol;
        string sourceMember = containingMethod?.Name ?? "<unknown>";
        Dictionary<string, (int Position, CSharpBuilderAssignment Assignment)> assignmentsByProperty =
            new(StringComparer.Ordinal);

        foreach (SyntaxNode node in scope.DescendantNodes())
        {
            if (node.SpanStart >= invocation.SpanStart) continue;
            if (!ReferenceEquals(FindExecutableScope(node), scope)) continue;
            if (!SharesControlFlowRegion(node, invocation, scope)) continue;

            CSharpBuilderAssignment? assignment = node switch
            {
                AssignmentExpressionSyntax assignmentExpression => ParseAssignment(
                    assignmentExpression,
                    target.Value,
                    sourceMember,
                    context.SemanticModel,
                    cancellationToken),
                InvocationExpressionSyntax mutationInvocation => ParseMutation(
                    mutationInvocation,
                    target.Value,
                    sourceMember,
                    context.SemanticModel,
                    cancellationToken),
                _ => null
            };
            if (assignment == null) continue;

            assignmentsByProperty[assignment.Value.PropertyReference] = (node.SpanStart, assignment.Value);
        }

        if (assignmentsByProperty.Count == 0) return null;

        ImmutableArray<CSharpBuilderAssignment> assignments =
        [
            ..assignmentsByProperty.Values
                .OrderBy(static value => value.Position)
                .Select(static value => value.Assignment)
        ];
        FileLinePositionSpan boundarySpan = invocation.GetLocation().GetLineSpan();
        return new CSharpBuilderBoundary(
            invocation.SyntaxTree.FilePath,
            boundarySpan.StartLinePosition.Line + 1,
            assignments);
    }

    private static CSharpBuilderAssignment? ParseAssignment(
        AssignmentExpressionSyntax assignment,
        TargetIdentity target,
        string sourceMember,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        IPropertySymbol? property = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol
            as IPropertySymbol;
        if (property == null || !AssignmentTargets(assignment, target, semanticModel, cancellationToken))
            return null;

        IFieldSymbol? propertyField = FindAvaloniaPropertyField(property);
        if (propertyField == null) return null;

        return CreateAssignment(
            assignment,
            propertyField,
            "CLRSetter",
            assignment.Right.ToString(),
            sourceMember,
            ResolveResourceKey(assignment.Right, semanticModel, cancellationToken));
    }

    private static CSharpBuilderAssignment? ParseMutation(
        InvocationExpressionSyntax invocation,
        TargetIdentity target,
        string sourceMember,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        IMethodSymbol? method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
            as IMethodSymbol;
        if (method == null) return null;

        string methodName = method.Name;
        if (methodName is "SetValue" or "SetCurrentValue" or "ClearValue" or "Bind")
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
                || !MatchesTarget(memberAccess.Expression, target, semanticModel, cancellationToken)
                || invocation.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            IFieldSymbol? propertyField = semanticModel.GetSymbolInfo(
                invocation.ArgumentList.Arguments[0].Expression,
                cancellationToken).Symbol as IFieldSymbol;
            if (propertyField == null || !IsAvaloniaPropertyType(propertyField.Type)) return null;

            string operation = methodName switch
            {
                "SetValue" => "SetValue",
                "SetCurrentValue" => "SetCurrentValue",
                "ClearValue" => "ClearValue",
                "Bind" => "Binding",
                _ => throw new InvalidOperationException()
            };
            string valueExpression = invocation.ArgumentList.Arguments.Count >= 2
                ? invocation.ArgumentList.Arguments[1].Expression.ToString()
                : "<cleared>";
            return CreateAssignment(
                invocation,
                propertyField,
                operation,
                valueExpression,
                sourceMember,
                null);
        }

        if (!method.IsStatic
            || !methodName.StartsWith("Set", StringComparison.Ordinal)
            || invocation.ArgumentList.Arguments.Count < 2
            || !MatchesTarget(
                invocation.ArgumentList.Arguments[0].Expression,
                target,
                semanticModel,
                cancellationToken))
        {
            return null;
        }

        string propertyFieldName = methodName[3..] + "Property";
        IFieldSymbol? attachedProperty = FindAvaloniaPropertyField(method.ContainingType, propertyFieldName);
        if (attachedProperty == null) return null;

        return CreateAssignment(
            invocation,
            attachedProperty,
            "AttachedProperty",
            invocation.ArgumentList.Arguments[1].Expression.ToString(),
            sourceMember,
            null);
    }

    private static CSharpBuilderAssignment CreateAssignment(
        SyntaxNode source,
        IFieldSymbol propertyField,
        string operation,
        string valueExpression,
        string sourceMember,
        string? resourceKey)
    {
        FileLinePositionSpan lineSpan = source.GetLocation().GetLineSpan();
        string propertyReference = propertyField.ContainingType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat) + "." + propertyField.Name;
        return new CSharpBuilderAssignment(
            propertyReference,
            operation,
            valueExpression,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1,
            sourceMember,
            resourceKey);
    }

    private static bool AssignmentTargets(
        AssignmentExpressionSyntax assignment,
        TargetIdentity target,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
            return MatchesTarget(memberAccess.Expression, target, semanticModel, cancellationToken);

        InitializerExpressionSyntax? initializer = assignment.Parent as InitializerExpressionSyntax;
        if (initializer?.Parent is BaseObjectCreationExpressionSyntax objectCreation)
        {
            ISymbol? creationTarget = ObjectCreationTarget(
                objectCreation,
                semanticModel,
                cancellationToken);
            return target.Symbol != null
                   && SymbolEqualityComparer.Default.Equals(creationTarget, target.Symbol);
        }

        return target.IsThis;
    }

    private static ISymbol? ObjectCreationTarget(
        BaseObjectCreationExpressionSyntax objectCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SyntaxNode? parent = objectCreation.Parent;
        if (parent is EqualsValueClauseSyntax equalsValue)
            parent = equalsValue.Parent;

        return parent is VariableDeclaratorSyntax declarator
            ? semanticModel.GetDeclaredSymbol(declarator, cancellationToken)
            : null;
    }

    private static TargetIdentity? CreateTargetIdentity(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is ThisExpressionSyntax)
            return new TargetIdentity(null, true);

        ISymbol? symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        return symbol == null ? null : new TargetIdentity(symbol, false);
    }

    private static bool MatchesTarget(
        ExpressionSyntax expression,
        TargetIdentity target,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (target.IsThis) return expression is ThisExpressionSyntax;

        ISymbol? symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, target.Symbol);
    }

    private static IFieldSymbol? FindAvaloniaPropertyField(IPropertySymbol property)
    {
        for (INamedTypeSymbol? type = property.ContainingType; type != null; type = type.BaseType)
        {
            IFieldSymbol? field = FindAvaloniaPropertyField(type, property.Name + "Property");
            if (field != null) return field;
        }

        return null;
    }

    private static IFieldSymbol? FindAvaloniaPropertyField(INamedTypeSymbol type, string fieldName)
    {
        foreach (IFieldSymbol field in type.GetMembers(fieldName).OfType<IFieldSymbol>())
        {
            if (field.IsStatic && IsAvaloniaPropertyType(field.Type)) return field;
        }

        return null;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current != null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (current.ToDisplayString() == "Avalonia.AvaloniaProperty") return true;
        }

        return false;
    }

    private static string? ResolveResourceKey(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        IPropertySymbol? property = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol
            as IPropertySymbol;
        if (property == null) return null;

        foreach (SyntaxReference syntaxReference in property.DeclaringSyntaxReferences)
        {
            SyntaxNode declaration = syntaxReference.GetSyntax(cancellationToken);
            foreach (MemberAccessExpressionSyntax access in declaration.DescendantNodesAndSelf()
                         .OfType<MemberAccessExpressionSyntax>())
            {
                string accessorName = access.Name.Identifier.ValueText;
                if (!accessorName.StartsWith("Axaml", StringComparison.Ordinal)
                    || accessorName.Length == "Axaml".Length)
                {
                    continue;
                }

                return accessorName["Axaml".Length..] + "." + property.Name;
            }
        }

        return null;
    }

    private static bool SharesControlFlowRegion(
        SyntaxNode source,
        SyntaxNode boundary,
        SyntaxNode executableScope)
    {
        foreach (SyntaxNode ancestor in source.Ancestors())
        {
            if (ReferenceEquals(ancestor, executableScope)) break;

            SyntaxNode? sourceRegion = ancestor switch
            {
                IfStatementSyntax ifStatement => BranchContaining(ifStatement, source),
                SwitchSectionSyntax switchSection => switchSection,
                ForStatementSyntax forStatement => forStatement.Statement,
                ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
                ForEachVariableStatementSyntax forEachVariableStatement => forEachVariableStatement.Statement,
                WhileStatementSyntax whileStatement => whileStatement.Statement,
                DoStatementSyntax doStatement => doStatement.Statement,
                CatchClauseSyntax catchClause => catchClause,
                FinallyClauseSyntax finallyClause => finallyClause,
                _ => null
            };
            if (sourceRegion != null && !sourceRegion.Span.Contains(boundary.Span))
                return false;
        }

        return true;
    }

    private static StatementSyntax? BranchContaining(IfStatementSyntax ifStatement, SyntaxNode source)
    {
        if (ifStatement.Statement.Span.Contains(source.Span)) return ifStatement.Statement;
        return ifStatement.Else?.Statement.Span.Contains(source.Span) == true
            ? ifStatement.Else.Statement
            : null;
    }

    private static SyntaxNode? FindExecutableScope(SyntaxNode node)
    {
        foreach (SyntaxNode ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax)
            {
                return ancestor;
            }
        }

        return null;
    }

    private static string? InvocationName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };

    private readonly record struct TargetIdentity(ISymbol? Symbol, bool IsThis);
}
