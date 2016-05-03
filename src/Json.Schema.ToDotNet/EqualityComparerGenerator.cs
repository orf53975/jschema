﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.
// Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Json.Schema.ToDotNet
{
    /// <summary>
    /// Generates classes that implement <see cref="System.Collections.Generic.IEqualityComparer{T}"/>.
    /// </summary>
    internal class EqualityComparerGenerator
    {
        private const string EqualityComparerSuffix = "EqualityComparer";
        private const string EqualityComparerInterfaceName = "I" + EqualityComparerSuffix;

        private const string EqualsMethodName = "Equals";
        private const string GetHashCodeMethodName = "GetHashCode";

        private const string ReferenceEqualsMethodName = "ReferenceEquals";

        private const string IntTypeAlias = "int";
        private const string ObjectTypeName = "Object";

        private const string CountPropertyName = "Count";
        private const string InstancePropertyName = "Instance";
        private const string KeyPropertyName = "Key";
        private const string ValuePropertyName = "Value";

        private const string FirstEqualsAgumentName = "left";
        private const string SecondEqualsArgumentName = "right";
        private const string GetHashCodeArgumentName = "obj";

        private const string GetHashCodeResultVariableName = "result";

        private const int GetHashCodeSeedValue = 17;
        private const int GetHashCodeCombiningValue = 31;

        private readonly string _copyrightNotice;
        private readonly string _namespaceName;

        private string _className;
        private TypeSyntax _classType;
        private PropertyInfoDictionary _propertyInfoDictionary;

        private readonly LocalVariableNameGenerator _localVariableNameGenerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="EqualityComparerGenerator"/> class.
        /// </summary>
        /// <param name="copyrightNotice">
        /// The copyright notice to display at the top of the file, or null if there is
        /// no copyright notice.
        /// </param>
        /// <param name="namespaceName">
        /// The name of the namespace into which the classes generated by this object
        /// are to be placed.
        /// </param>
        internal EqualityComparerGenerator(
            string copyrightNotice,
            string namespaceName)
        {
            _copyrightNotice = copyrightNotice;
            _namespaceName = namespaceName;

            _localVariableNameGenerator = new LocalVariableNameGenerator();
        }

        /// <summary>
        /// Gets a string containing the name of the equality comparer class generated by
        /// the most recent class to <see cref="Generate(string, PropertyInfoDictionary)"/>.
        /// </summary>
        internal static string GetEqualityComparerClassName(string className)
        {
            return className + EqualityComparerSuffix;
        }

        /// <summary>
        /// Generates a class that implements <see cref="System.Collections.Generic.IEqualityComparer{T}"/>
        /// for the specified class.
        /// </summary>
        /// <param name="className">
        /// The name of the class whose equality comparer class is to be generated.
        /// </param>
        /// <param name="propertyInfoDictionary">
        /// An object containing information about each property in the class specified by <paramref name="className"/>.
        /// </param>
        /// <returns>
        /// A string containing the text of the generated equality comparer class.
        /// </returns>
        internal string Generate(string className, PropertyInfoDictionary propertyInfoDictionary)
        {
            _className = className;
            _propertyInfoDictionary = propertyInfoDictionary;

            _classType = SyntaxFactory.ParseTypeName(_className);
            _localVariableNameGenerator.Reset();

            string comparerClassName = GetEqualityComparerClassName(_className);

            var comparerInterface = MakeComparerBaseType();

            ClassDeclarationSyntax classDeclaration =
                SyntaxFactory.ClassDeclaration(comparerClassName)
                    .AddModifiers(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.SealedKeyword))
                    .AddBaseListTypes(comparerInterface)
                    .AddMembers(
                        GenerateInstanceProperty(),
                        GenerateEqualsMethod(),
                        GenerateGetHashCodeMethod());

            var usings = new List<string>
            {
                "System",                       // For Object.
                "System.Collections.Generic"    // For IEqualityComparer<T>
            };

            return classDeclaration.Format(
                _copyrightNotice,
                usings,
                _namespaceName,
                MakeSummaryComment());
        }

        private BaseTypeSyntax MakeComparerBaseType()
        {
            return SyntaxFactory.SimpleBaseType(
                SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(EqualityComparerInterfaceName),
                    SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(
                        new TypeSyntax[] {
                            SyntaxFactory.ParseTypeName(_className)
                        }))));
        }

        private string MakeSummaryComment()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Resources.EqualityComparerSummary,
                _className);
        }

        private MemberDeclarationSyntax GenerateInstanceProperty()
        {
            TypeSyntax comparerType = SyntaxFactory.ParseTypeName(GetEqualityComparerClassName(_className));

            // public static readonly ComparerType Instance = new ComparerType();
            return SyntaxFactory.FieldDeclaration(
                default(SyntaxList<AttributeListSyntax>),
                SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)),
                SyntaxFactory.VariableDeclaration(comparerType,
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(InstancePropertyName),
                            default(BracketedArgumentListSyntax),
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.ObjectCreationExpression(
                                    comparerType,
                                    SyntaxHelper.ArgumentList(),
                                    default(InitializerExpressionSyntax)))))));
        }

        private MemberDeclarationSyntax GenerateEqualsMethod()
        {
            return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                EqualsMethodName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddParameterListParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier(FirstEqualsAgumentName))
                        .WithType(_classType),
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier(SecondEqualsArgumentName))
                        .WithType(_classType))
                .AddBodyStatements(
                    GenerateEqualsBody());
        }

        private MemberDeclarationSyntax GenerateGetHashCodeMethod()
        {
            return SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                GetHashCodeMethodName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddParameterListParameters(
                    SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier(GetHashCodeArgumentName))
                        .WithType(_classType))
                .AddBodyStatements(
                    GenerateGetHashCodeBody());
        }

        private StatementSyntax[] GenerateEqualsBody()
        {
            var statements = new List<StatementSyntax>();

            statements.Add(GenerateReferenceEqualityTest());
            statements.Add(NullCheckTest());
            statements.AddRange(GenerateComparerDeclarations());
            statements.AddRange(GeneratePropertyComparisons());

            return statements.ToArray();
        }

        private IfStatementSyntax GenerateReferenceEqualityTest()
        {
            return SyntaxFactory.IfStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(ReferenceEqualsMethodName),
                    SyntaxHelper.ArgumentList(
                        SyntaxFactory.IdentifierName(FirstEqualsAgumentName),
                        SyntaxFactory.IdentifierName(SecondEqualsArgumentName))),
                SyntaxFactory.Block(
                    SyntaxHelper.Return(true)));
        }

        private IfStatementSyntax NullCheckTest()
        {
            return SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LogicalOrExpression,
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName(ReferenceEqualsMethodName),
                        SyntaxHelper.ArgumentList(
                            SyntaxFactory.IdentifierName(FirstEqualsAgumentName),
                            SyntaxHelper.Null())),
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.IdentifierName(ReferenceEqualsMethodName),
                        SyntaxHelper.ArgumentList(
                            SyntaxFactory.IdentifierName(SecondEqualsArgumentName),
                            SyntaxHelper.Null()))),
                SyntaxFactory.Block(
                    SyntaxHelper.Return(false)));
        }

        private IList<StatementSyntax> GenerateComparerDeclarations()
        {
            var statements = new List<StatementSyntax>();

            IEnumerable<string> comparerTypeNames = _propertyInfoDictionary.Keys
                .Where(key => _propertyInfoDictionary[key].ComparisonKind == ComparisonKind.EqualityComparerEquals)
                .Select(key => GetComparerTypeName(key))
                .Distinct()
                .OrderBy(ctn => ctn);

            foreach (string comparerTypeName in comparerTypeNames)
            {
                statements.Add(GenerateComparerDeclaration(comparerTypeName));
            }

            return statements;
        }

        private string GetComparerTypeName(string index)
        {
            return GetEqualityComparerClassName(
                _propertyInfoDictionary[index].Type.ToString());
        }

        private StatementSyntax GenerateComparerDeclaration(string comparerTypeName)
        {
            string comparerVariableName = comparerTypeName.ToCamelCase();

            // var comparer = new XEqualityComparer();
            return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxHelper.Var(),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(comparerVariableName),
                            default(BracketedArgumentListSyntax),
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.ObjectCreationExpression(
                                    SyntaxFactory.ParseTypeName(comparerTypeName),
                                    SyntaxHelper.ArgumentList(),
                                    default(InitializerExpressionSyntax)))))));
        }

        private IList<StatementSyntax> GeneratePropertyComparisons()
        {
            var statements = new List<StatementSyntax>();

            foreach (string propertyName in _propertyInfoDictionary.GetPropertyNames())
            {
                statements.Add(
                    GeneratePropertyComparison(
                        propertyName,
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(FirstEqualsAgumentName),
                            SyntaxFactory.IdentifierName(propertyName)),
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(SecondEqualsArgumentName),
                            SyntaxFactory.IdentifierName(propertyName))));
            }

            // All comparisons succeeded.
            statements.Add(SyntaxHelper.Return(true));

            return statements;
        }

        private StatementSyntax GeneratePropertyComparison(
            string propertyName,
            ExpressionSyntax left,
            ExpressionSyntax right)
        {
            ComparisonKind comparisonKind = _propertyInfoDictionary[propertyName].ComparisonKind;
            switch (comparisonKind)
            {
                case ComparisonKind.OperatorEquals:
                    return GeneratorOperatorEqualsComparison(left, right);

                case ComparisonKind.ObjectEquals:
                    return GenerateObjectEqualsComparison(left, right);

                case ComparisonKind.EqualityComparerEquals:
                    return GenerateEqualityEqualsComparison(propertyName, left, right);

                case ComparisonKind.Collection:
                    return MakeCollectionEqualsTest(propertyName, left, right);

                case ComparisonKind.Dictionary:
                    return MakeDictionaryEqualsTest(propertyName, left, right);

                default:
                    throw new ArgumentException($"Property {propertyName} has unknown comparison type {comparisonKind}.");
            }
        }

        private StatementSyntax GeneratorOperatorEqualsComparison(ExpressionSyntax left, ExpressionSyntax right)
        {
            return SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    left,
                    right),
                SyntaxFactory.Block(SyntaxHelper.Return(false)));
        }

        private StatementSyntax GenerateObjectEqualsComparison(ExpressionSyntax left, ExpressionSyntax right)
        {
            // if (!(Object.Equals(left.Prop, right.Prop))
            return SyntaxFactory.IfStatement(
                SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(ObjectTypeName),
                            SyntaxFactory.IdentifierName(EqualsMethodName)),
                        SyntaxHelper.ArgumentList(left, right))),
                SyntaxFactory.Block(SyntaxHelper.Return(false)));
        }

        private StatementSyntax GenerateEqualityEqualsComparison(
            string propertyName,
            ExpressionSyntax left,
            ExpressionSyntax right)
        {
            string comparerVariableName = GetComparerTypeName(propertyName).ToCamelCase();

            // if (!(comparer.Equals(left.Prop, right.Prop))
            return SyntaxFactory.IfStatement(
                SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(comparerVariableName),
                            SyntaxFactory.IdentifierName(EqualsMethodName)),
                        SyntaxHelper.ArgumentList(left, right))),
                SyntaxFactory.Block(SyntaxHelper.Return(false)));
        }

        private StatementSyntax MakeCollectionEqualsTest(
            string comparisonKindKey,
            ExpressionSyntax left,
            ExpressionSyntax right)
        {
            return SyntaxFactory.IfStatement(
                // if (!Object.ReferenceEquals(Prop, other.Prop))
                SyntaxHelper.AreDifferentObjects(left, right),
                SyntaxFactory.Block(
                    // if (Prop == null || other.Prop == null)
                    SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.LogicalOrExpression,
                            SyntaxHelper.IsNull(left),
                            SyntaxHelper.IsNull(right)),
                        SyntaxFactory.Block(SyntaxHelper.Return(false))),

                    // if (Prop.Count != other.Prop.Count)
                    SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.NotEqualsExpression,
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                left,
                                SyntaxFactory.IdentifierName(CountPropertyName)),
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                right,
                                SyntaxFactory.IdentifierName(CountPropertyName))),
                        SyntaxFactory.Block(SyntaxHelper.Return(false))),

                    CollectionIndexLoop(comparisonKindKey, left, right)
                    ));
        }

        private ForStatementSyntax CollectionIndexLoop(
            string comparisonKindKey,
            ExpressionSyntax left,
            ExpressionSyntax right)
        {
            // The name of the index variable used in the loop over elements.
            string indexVarName = _localVariableNameGenerator.GetNextLoopIndexVariableName();

            // The two elements that will be compared each time through the loop.
            ExpressionSyntax leftElement =
                SyntaxFactory.ElementAccessExpression(
                    left,
                    SyntaxHelper.BracketedArgumentList(
                        SyntaxFactory.IdentifierName(indexVarName)));

            ExpressionSyntax rightElement =
                SyntaxFactory.ElementAccessExpression(
                right,
                SyntaxHelper.BracketedArgumentList(
                    SyntaxFactory.IdentifierName(indexVarName)));

            // From the type of the element (primitive, object, list, or dictionary), create
            // the appropriate comparison, for example, "a == b", or "Object.Equals(a, b)".
            string elmentComparisonTypeKey = PropertyInfoDictionary.MakeElementKeyName(comparisonKindKey);

            StatementSyntax comparisonStatement = GeneratePropertyComparison(elmentComparisonTypeKey, leftElement, rightElement);

            return SyntaxFactory.ForStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName(IntTypeAlias),
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(indexVarName),
                            default(BracketedArgumentListSyntax),
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    SyntaxFactory.Literal(0)))))),
                SyntaxFactory.SeparatedList<ExpressionSyntax>(),
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.LessThanExpression,
                    SyntaxFactory.IdentifierName(indexVarName),
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        left,
                        SyntaxFactory.IdentifierName(CountPropertyName))),
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.PrefixUnaryExpression(
                        SyntaxKind.PreIncrementExpression,
                        SyntaxFactory.IdentifierName(indexVarName))),
                SyntaxFactory.Block(comparisonStatement));
        }

        private IfStatementSyntax MakeDictionaryEqualsTest(
            string propertyInfoKey,
            ExpressionSyntax left,
            ExpressionSyntax right)
        {
            string dictionaryElementVariableName = _localVariableNameGenerator.GetNextCollectionElementVariableName();
            string otherPropertyVariableName = _localVariableNameGenerator.GetNextCollectionElementVariableName();

            // Construct the key into the PropertyInfoDictionary so we can look up how
            // dictionary elements are to be compared.
            string valuePropertyInfoKey = PropertyInfoDictionary.MakeDictionaryItemKeyName(propertyInfoKey);
            TypeSyntax dictionaryValueType = _propertyInfoDictionary[valuePropertyInfoKey].Type;

            return SyntaxFactory.IfStatement(
                // if (!Object.ReferenceEquals(left, right))
                SyntaxHelper.AreDifferentObjects(left, right),
                SyntaxFactory.Block(
                    // if (left == null || right == null || left.Count != right.Count)
                    SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.LogicalOrExpression,
                            SyntaxHelper.IsNull(left),
                            SyntaxFactory.BinaryExpression(
                                SyntaxKind.LogicalOrExpression,
                                SyntaxHelper.IsNull(right),
                                SyntaxFactory.BinaryExpression(
                                    SyntaxKind.NotEqualsExpression,
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        left,
                                        SyntaxFactory.IdentifierName(CountPropertyName)),
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        right,
                                        SyntaxFactory.IdentifierName(CountPropertyName))))),
                        // return false;
                        SyntaxFactory.Block(SyntaxHelper.Return(false))),
                    // foreach (var value_0 in left)
                    SyntaxFactory.ForEachStatement(
                        SyntaxHelper.Var(),
                        dictionaryElementVariableName,
                        left,
                        SyntaxFactory.Block(
                            // var value_1;
                            SyntaxFactory.LocalDeclarationStatement(
                                default(SyntaxTokenList), // modifiers
                                SyntaxFactory.VariableDeclaration(
                                    dictionaryValueType,
                                    SyntaxFactory.SingletonSeparatedList(
                                        SyntaxFactory.VariableDeclarator(otherPropertyVariableName)))),
                            // if (!right.TryGetValue(value_0.Key, out value_1))
                            SyntaxFactory.IfStatement(
                                SyntaxFactory.PrefixUnaryExpression(
                                    SyntaxKind.LogicalNotExpression,
                                    SyntaxFactory.InvocationExpression(
                                        SyntaxFactory.MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            right,
                                            SyntaxFactory.IdentifierName("TryGetValue")),
                                        SyntaxFactory.ArgumentList(
                                            SyntaxFactory.SeparatedList(
                                                new ArgumentSyntax[]
                                                {
                                                    SyntaxFactory.Argument(
                                                        SyntaxFactory.MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            SyntaxFactory.IdentifierName(dictionaryElementVariableName),
                                                            SyntaxFactory.IdentifierName(KeyPropertyName))),
                                                    SyntaxFactory.Argument(
                                                        default(NameColonSyntax),
                                                        SyntaxFactory.Token(SyntaxKind.OutKeyword),
                                                        SyntaxFactory.IdentifierName(otherPropertyVariableName))

                                                })))),
                                // return false;
                                SyntaxFactory.Block(SyntaxHelper.Return(false))),

                            GeneratePropertyComparison(
                                    valuePropertyInfoKey,
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(dictionaryElementVariableName),
                                        SyntaxFactory.IdentifierName(ValuePropertyName)),
                                    SyntaxFactory.IdentifierName(otherPropertyVariableName))))));
        }

        private StatementSyntax[] GenerateGetHashCodeBody()
        {
            var statements = new List<StatementSyntax>();

            statements.Add(SyntaxFactory.IfStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(ReferenceEqualsMethodName),
                    SyntaxHelper.ArgumentList(
                        SyntaxFactory.IdentifierName(GetHashCodeArgumentName),
                        SyntaxHelper.Null())),
                SyntaxFactory.Block(
                    SyntaxHelper.Return(0))));

            statements.Add(SyntaxFactory.LocalDeclarationStatement(
                            SyntaxFactory.VariableDeclaration(
                                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.VariableDeclarator(
                                        SyntaxFactory.Identifier(GetHashCodeResultVariableName),
                                        default(BracketedArgumentListSyntax),
                                        SyntaxFactory.EqualsValueClause(
                                            SyntaxFactory.LiteralExpression(
                                                SyntaxKind.NumericLiteralExpression,
                                                SyntaxFactory.Literal(GetHashCodeSeedValue))))))));

            string[] propertyNames = _propertyInfoDictionary.GetPropertyNames();
            if (propertyNames.Any())
            {
                var uncheckedStatements = new List<StatementSyntax>();
                foreach (var propertyName in propertyNames)
                {
                    uncheckedStatements.Add(
                        GeneratePropertyHashCodeContribution(
                            propertyName,
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName(GetHashCodeArgumentName),
                                SyntaxFactory.IdentifierName(propertyName))));
                }

                statements.Add(SyntaxFactory.CheckedStatement(
                    SyntaxKind.UncheckedStatement,
                    SyntaxFactory.Block(uncheckedStatements)));
            }

            statements.Add(SyntaxFactory.ReturnStatement(
                                SyntaxFactory.IdentifierName(GetHashCodeResultVariableName)));

            return statements.ToArray();
        }

        private StatementSyntax GeneratePropertyHashCodeContribution(string hashKindKey, ExpressionSyntax expression)
        {
            HashKind hashKind = _propertyInfoDictionary[hashKindKey].HashKind;
            switch (hashKind)
            {
                case HashKind.ScalarValueType:
                    return GenerateScalarHashCodeContribution(expression);

                case HashKind.ScalarReferenceType:
                    return GenerateScalarReferenceTypeHashCodeContribution(expression);

                case HashKind.Collection:
                    return GenerateCollectionHashCodeContribution(hashKindKey, expression);

                case HashKind.Dictionary:
                    return GenerateDictionaryHashCodeContribution(expression);

                default:
                    throw new ArgumentException($"Property {hashKindKey} has unknown comparison type {hashKind}.");
            }
        }

        private StatementSyntax GenerateScalarHashCodeContribution(ExpressionSyntax expression)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                        SyntaxFactory.BinaryExpression(
                            SyntaxKind.AddExpression,
                            SyntaxFactory.ParenthesizedExpression(
                                SyntaxFactory.BinaryExpression(
                                    SyntaxKind.MultiplyExpression,
                                    SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.NumericLiteralExpression,
                                        SyntaxFactory.Literal(GetHashCodeCombiningValue)))),
                            SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    expression,
                                    SyntaxFactory.IdentifierName(GetHashCodeMethodName))))));
        }

        private StatementSyntax GenerateScalarReferenceTypeHashCodeContribution(ExpressionSyntax expression)
        {
            return SyntaxFactory.IfStatement(
                SyntaxHelper.IsNotNull(expression),
                SyntaxFactory.Block(GenerateScalarHashCodeContribution(expression)));
        }

        private StatementSyntax GenerateCollectionHashCodeContribution(
            string hashKindKey,
            ExpressionSyntax expression)
        {
            string collectionElementVariableName = _localVariableNameGenerator.GetNextCollectionElementVariableName();

            // From the type of the element (primitive, object, list, or dictionary), create
            // the appropriate hash generation code.
            string elementHashTypeKey = PropertyInfoDictionary.MakeElementKeyName(hashKindKey);

            StatementSyntax hashCodeContribution =
                GeneratePropertyHashCodeContribution(
                    elementHashTypeKey,
                    SyntaxFactory.IdentifierName(collectionElementVariableName));

            return SyntaxFactory.IfStatement(
                SyntaxHelper.IsNotNull(expression),
                SyntaxFactory.Block(
                    SyntaxFactory.ForEachStatement(
                        SyntaxHelper.Var(),
                        collectionElementVariableName,
                        expression,
                        SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                                    SyntaxFactory.BinaryExpression(
                                        SyntaxKind.MultiplyExpression,
                                        SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                                        SyntaxFactory.LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            SyntaxFactory.Literal(GetHashCodeCombiningValue))))),
                            hashCodeContribution))));
        }

        private StatementSyntax GenerateDictionaryHashCodeContribution(ExpressionSyntax expression)
        {
            string xorValueVariableName = _localVariableNameGenerator.GetNextXorVariableName();
            string collectionElementVariableName = _localVariableNameGenerator.GetNextCollectionElementVariableName();

            return SyntaxFactory.IfStatement(
                SyntaxHelper.IsNotNull(expression),
                SyntaxFactory.Block(
                    // int xor_0 = 0;
                    SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(
                                    SyntaxFactory.Identifier(xorValueVariableName),
                                    default(BracketedArgumentListSyntax),
                                    SyntaxFactory.EqualsValueClause(
                                        SyntaxFactory.LiteralExpression(
                                            SyntaxKind.NumericLiteralExpression,
                                            SyntaxFactory.Literal(0)))))))
                        .WithLeadingTrivia(
                            SyntaxFactory.ParseLeadingTrivia(Resources.XorDictionaryComment)),

                    SyntaxFactory.ForEachStatement(
                        SyntaxHelper.Var(),
                        collectionElementVariableName,
                        expression,
                        SyntaxFactory.Block(
                            // xor_0 ^= value_0.Key.GetHashCode();
                            Xor(xorValueVariableName, collectionElementVariableName, KeyPropertyName),
                            SyntaxFactory.IfStatement(
                                SyntaxHelper.IsNotNull(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(collectionElementVariableName),
                                        SyntaxFactory.IdentifierName(ValuePropertyName))),
                                SyntaxFactory.Block(
                                    Xor(xorValueVariableName, collectionElementVariableName, ValuePropertyName))))),

                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                            SyntaxFactory.BinaryExpression(
                                SyntaxKind.AddExpression,
                                SyntaxFactory.ParenthesizedExpression(
                                    SyntaxFactory.BinaryExpression(
                                        SyntaxKind.MultiplyExpression,
                                            SyntaxFactory.IdentifierName(GetHashCodeResultVariableName),
                                                SyntaxFactory.LiteralExpression(
                                                    SyntaxKind.NumericLiteralExpression,
                                                    SyntaxFactory.Literal(GetHashCodeCombiningValue)))),
                                SyntaxFactory.IdentifierName(xorValueVariableName))))));
        }

        private StatementSyntax Xor(string xorValueVariableName, string loopVariableName, string keyValuePairMemberName)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.ExclusiveOrAssignmentExpression,
                    SyntaxFactory.IdentifierName(xorValueVariableName),
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName(loopVariableName),
                                SyntaxFactory.IdentifierName(keyValuePairMemberName)),
                        SyntaxFactory.IdentifierName(GetHashCodeMethodName)))));
        }
    }
}