﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PropertyChanged.SourceGenerator.Analysis
{
    public partial class Analyser
    {
        private bool TryFindPropertyRaiseMethod(
            INamedTypeSymbol typeSymbol,
            TypeAnalysis typeAnalysis,
            IReadOnlyList<TypeAnalysis> baseTypeAnalyses, 
            Configuration config)
        {
            // Try and find out how we raise the PropertyChanged event
            // 1. If noone's defined the PropertyChanged event yet, we'll define it ourselves
            // 2. Otherwise, try and find a method to raise the event:
            //   a. If PropertyChanged is in a base class, we'll need to abort if we can't find one
            //   b. If PropertyChanged is in our class, we'll just define one and call it

            // They might have defined the event but not the interface, so we'll just look for the event by its
            // signature
            var eventSymbol = TypeAndBaseTypes(typeSymbol)
                .SelectMany(x => x.GetMembers("PropertyChanged"))
                .OfType<IEventSymbol>()
                .FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.Type, this.propertyChangedEventHandlerSymbol) &&
                    !x.IsStatic);

            bool isGeneratingAnyParent = baseTypeAnalyses.Any(x => x.CanGenerate);

            // If there's no event, the base type in our hierarchy is defining it
            typeAnalysis.RequiresEvent = eventSymbol == null && !isGeneratingAnyParent;

            // Try and find a method with a name we recognise and a signature we know how to call
            // We prioritise the method name over things like the signature or where in the type hierarchy
            // it is. One we've found any method with a name we're looking for, stop: it's most likely they've 
            // just messed up the signature
            RaisePropertyChangedMethodSignature? signature = null;
            string? methodName = null;
            foreach (string name in config.RaisePropertyChangedMethodNames)
            {
                var methods = TypeAndBaseTypes(typeSymbol)
                    .SelectMany(x => x.GetMembers(name))
                    .OfType<IMethodSymbol>()
                    .Where(x => !x.IsOverride && !x.IsStatic)
                    .ToList();
                if (methods.Count > 0)
                {
                    signature = FindCallableOverload(methods);
                    if (signature != null)
                    {
                        methodName = name;
                    }
                    else
                    {
                        this.diagnostics.ReportCouldNotFindCallableRaisePropertyChangedOverload(typeSymbol, name);
                        return false;
                    }
                    break;
                }
            }

            if (signature != null)
            {
                // We found a method which we know how to call
                typeAnalysis.RequiresRaisePropertyChangedMethod = false;
                typeAnalysis.RaisePropertyChangedMethodName = methodName!;
                typeAnalysis.RaisePropertyChangedMethodSignature = signature.Value;
            }
            else
            {
                // The base type in our hierarchy is defining its own
                // Make sure that that type can actually access the event, if it's pre-existing
                if (eventSymbol != null && !isGeneratingAnyParent &&
                    !SymbolEqualityComparer.Default.Equals(eventSymbol.ContainingType, typeSymbol))
                {
                    this.diagnostics.ReportCouldNotFindRaisePropertyChangedMethod(typeSymbol);
                    return false;
                }

                typeAnalysis.RequiresRaisePropertyChangedMethod = !isGeneratingAnyParent;
                typeAnalysis.RaisePropertyChangedMethodName = config.RaisePropertyChangedMethodNames[0];
                typeAnalysis.RaisePropertyChangedMethodSignature = RaisePropertyChangedMethodSignature.Default;
            }

            return true;

            RaisePropertyChangedMethodSignature? FindCallableOverload(List<IMethodSymbol> methods)
            {
                methods.RemoveAll(x => !this.IsAccessibleNormalMethod(x, typeSymbol));

                // We care about the order in which we choose an overload, which unfortunately means we're quadratic
                if (methods.Any(x => x.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(x.Parameters[0].Type, this.propertyChangedEventArgsSymbol) &&
                    IsNormalParameter(x.Parameters[0])))
                {
                    return new RaisePropertyChangedMethodSignature(RaisePropertyChangedNameType.PropertyChangedEventArgs, hasOldAndNew: false);
                }

                if (methods.Any(x => x.Parameters.Length == 1 &&
                    x.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                    IsNormalParameter(x.Parameters[0])))
                {
                    return new RaisePropertyChangedMethodSignature(RaisePropertyChangedNameType.String, hasOldAndNew: false);
                }

                if (methods.Any(x => x.Parameters.Length == 3 &&
                    SymbolEqualityComparer.Default.Equals(x.Parameters[0].Type, this.propertyChangedEventArgsSymbol) &&
                    IsNormalParameter(x.Parameters[0]) &&
                    x.Parameters[1].Type.SpecialType == SpecialType.System_Object &&
                    IsNormalParameter(x.Parameters[1]) &&
                    x.Parameters[2].Type.SpecialType == SpecialType.System_Object &&
                    IsNormalParameter(x.Parameters[2])))
                {
                    return new RaisePropertyChangedMethodSignature(RaisePropertyChangedNameType.PropertyChangedEventArgs, hasOldAndNew: true);
                }
                if (methods.Any(x => x.Parameters.Length == 3 &&
                    x.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                    IsNormalParameter(x.Parameters[0]) &&
                    x.Parameters[1].Type.SpecialType == SpecialType.System_Object &&
                    IsNormalParameter(x.Parameters[1]) &&
                    x.Parameters[2].Type.SpecialType == SpecialType.System_Object &&
                    IsNormalParameter(x.Parameters[2])))
                {
                    return new RaisePropertyChangedMethodSignature(RaisePropertyChangedNameType.String, hasOldAndNew: true);
                }

                return null;
            }
        }

        private OnPropertyNameChangedInfo? FindOnPropertyNameChangedMethod(INamedTypeSymbol typeSymbol, IPropertySymbol property) =>
            this.FindOnPropertyNameChangedMethod(typeSymbol, property.Name, property.Type, property.ContainingType);

        /// <param name="typeSymbol">Type we're currently analysing</param>
        /// <param name="name">Name of the property to find an OnPropertyNameChanged method for</param>
        /// <param name="memberType">Type of the property</param>
        /// <param name="containingType">Type containing the property (may be a base type)</param>
        /// <returns></returns>
        private OnPropertyNameChangedInfo? FindOnPropertyNameChangedMethod(
            INamedTypeSymbol typeSymbol, 
            string name,
            ITypeSymbol memberType,
            INamedTypeSymbol containingType)
        {
            string onChangedMethodName = $"On{name}Changed";
            var methods = containingType.GetMembers(onChangedMethodName)
                .OfType<IMethodSymbol>()
                .Where(x => !x.IsOverride && !x.IsStatic)
                .ToList();

            OnPropertyNameChangedInfo? result = null;
            if (methods.Count > 0)
            {
                // FindCallableOverload might remove some...
                var firstMethod = methods[0];
                var signature = FindCallableOverload(methods);
                if (signature != null)
                {
                    result = new OnPropertyNameChangedInfo(onChangedMethodName, signature.Value);
                }
                else
                {
                    this.diagnostics.ReportInvalidOnPropertyNameChangedSignature(name, onChangedMethodName, firstMethod);
                }
            }

            return result;

            OnPropertyNameChangedSignature? FindCallableOverload(List<IMethodSymbol> methods)
            {
                methods.RemoveAll(x => !this.IsAccessibleNormalMethod(x, typeSymbol));

                if (methods.Any(x => x.Parameters.Length == 2 &&
                    IsNormalParameter(x.Parameters[0]) &&
                    IsNormalParameter(x.Parameters[1]) &&
                    SymbolEqualityComparer.Default.Equals(x.Parameters[0].Type, x.Parameters[1].Type) &&
                    this.compilation.HasImplicitConversion(memberType, x.Parameters[0].Type)))
                {
                    return OnPropertyNameChangedSignature.OldAndNew;
                }

                if (methods.Any(x => x.Parameters.Length == 0))
                {
                    return OnPropertyNameChangedSignature.Parameterless;
                }

                return null;
            }
        }

        private bool IsAccessibleNormalMethod(IMethodSymbol method, ITypeSymbol typeSymbol) =>
            !method.IsGenericMethod &&
            method.ReturnsVoid &&
            this.compilation.IsSymbolAccessibleWithin(method, typeSymbol);

        private static bool IsNormalParameter(IParameterSymbol parameter) =>
            parameter.RefKind == RefKind.None;
    }
}
