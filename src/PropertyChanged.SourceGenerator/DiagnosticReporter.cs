﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PropertyChanged.SourceGenerator
{
    public class DiagnosticReporter
    {
        public List<Diagnostic> Diagnostics { get; } = new();

        public bool HasDiagnostics { get; private set; }
        public bool HasErrors { get; private set; }

        private static readonly DiagnosticDescriptor couldNotFindInpc = CreateDescriptor(
            "INPC001",
            "Unable to find INotifyPropertyChanged",
            "Unable to find the interface System.ComponentModel.INotifyPropertyChanged. Ensure you are referencing the containing assembly");
        public void ReportCouldNotFindInpc()
        {
            this.AddDiagnostic(couldNotFindInpc);
        }

        private static readonly DiagnosticDescriptor typeIsNotPartial = CreateDescriptor(
            "INPC002",
            "Type is not partial",
            "Type '{0}' must be partial in order for PropertyChanged.SourceGenerator to generate properties");
        public void ReportTypeIsNotPartial(INamedTypeSymbol typeSymbol)
        {
            this.AddDiagnostic(typeIsNotPartial, typeSymbol.Locations, typeSymbol.Name);
        }

        private static readonly DiagnosticDescriptor memberWithNameAlreadyExists = CreateDescriptor(
            "INPC003",
            "Member with this name already exists",
            "Attempted to generate property '{0}' for member '{1}', but a member with that name already exists. Skipping this property");

        public void ReportMemberWithNameAlreadyExists(ISymbol symbol, string name)
        {
            this.AddDiagnostic(memberWithNameAlreadyExists, symbol.Locations, name, symbol.Name);
        }

        private static readonly DiagnosticDescriptor anotherMemberHasSameGeneratedName = CreateDescriptor(
            "INPC004",
            "Another member has the same generated name as this one",
            "Member '{0}' will have the same generated property name '{1}' as member '{2}'. Skipping both properties");

        public void ReportAnotherMemberHasSameGeneratedName(ISymbol thisMember, ISymbol otherMember, string name)
        {
            this.AddDiagnostic(anotherMemberHasSameGeneratedName, thisMember.Locations, thisMember.Name, name, otherMember.Name);
        }

        private static readonly DiagnosticDescriptor incompatiblePropertyAccessibilities = CreateDescriptor(
            "INPC005",
            "Incompatible property accessibilities",
            "C# propertes may not have an internal getter and protected setter, or protected setter and internal getter. Defaulting both to protected internal");
        public void ReportIncomapatiblePropertyAccessibilities(ISymbol member, AttributeData notifyAttribute)
        {
            this.AddDiagnostic(incompatiblePropertyAccessibilities, AttributeLocations(notifyAttribute, member));
        }

        private static readonly DiagnosticDescriptor couldNotFindCallableRaisePropertyChangedOverload = CreateDescriptor(
            "INPC006",
            "Could not find callable method to raise PropertyChanged event",
            "Found one or more methods called '{0}' to raise the PropertyChanged event, but they had an unrecognised signatures or were inaccessible. Ignoring");
        public void ReportCouldNotFindCallableRaisePropertyChangedOverload(INamedTypeSymbol typeSymbol, string name)
        {
            this.AddDiagnostic(couldNotFindCallableRaisePropertyChangedOverload, typeSymbol.Locations, name);
        }

        private static readonly DiagnosticDescriptor couldNotFindRaisePropertyChangedMethod = CreateDescriptor(
            "INPC007",
            "Could not find method to raise PropertyChanged event",
            "Could not find any suitable methods to raise the PropertyChanged event defined on a base class");
        public void ReportCouldNotFindRaisePropertyChangedMethod(INamedTypeSymbol typeSymbol)
        {
            this.AddDiagnostic(couldNotFindRaisePropertyChangedMethod, typeSymbol.Locations);
        }

        private static readonly DiagnosticDescriptor alsoNotifyAttributeNotValidOnMember = CreateDescriptor(
            "INPC008",
            "AlsoNotify is not valid here",
            "[AlsoNotify] is only valid on members which also have [Notify]. Skipping");
        public void ReportAlsoNotifyAttributeNotValidOnMember(AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(alsoNotifyAttributeNotValidOnMember, AttributeLocations(attribute, member));
        }

        private static readonly DiagnosticDescriptor alsoNotifyPropertyDoesNotExist = CreateDescriptor(
            "INPC009",
            "AlsoNotify property name does not exist",
            "Unable to find a property called '{0}' on this type or its base types. This event will still be raised");
        public void ReportAlsoNotifyPropertyDoesNotExist(string alsoNotify, AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(alsoNotifyPropertyDoesNotExist, AttributeLocations(attribute, member), alsoNotify);
        }

        private static readonly DiagnosticDescriptor dependsOnPropertyDoesNotExist = CreateDescriptor(
            "INPC010",
            "DependsOn property name does not exist",
            "Unable to find a property called '{0}' on this type which was generated by PropertyChanged.SourceGenerator. Skipping");
        public void RaiseDependsOnPropertyDoesNotExist(string? dependsOn, AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(dependsOnPropertyDoesNotExist, AttributeLocations(attribute, member), dependsOn);
        }

        private static readonly DiagnosticDescriptor dependsOnAppliedToFieldWithoutNotify = CreateDescriptor(
            "INPC011",
            "DependsOn field does not have [Notify]",
            "[DependsOn] must only be applied to fields which also have [Notify]");
        public void RaiseDependsOnAppliedToFieldWithoutNotify(AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(dependsOnAppliedToFieldWithoutNotify, AttributeLocations(attribute, member));
        }

        private static readonly DiagnosticDescriptor alsoNotifyForSelf = CreateDescriptor(
            "INPC012",
            "AlsoNotify applied to self",
            "Property '{0}' cannot have an [AlsoNotify] attribute which refers to that same property");
        public void ReportAlsoNotifyForSelf(string alsoNotify, AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(alsoNotifyForSelf, AttributeLocations(attribute, member), alsoNotify);
        }

        private static readonly DiagnosticDescriptor invalidOnPropertyNameChangedSignature = CreateDescriptor(
            "INPC013",
            "Unable to find matching On{PropertyName}Changed",
            "Found one or more On{{PropertyName}}Changed methods called '{0}' for property '{1}', but none had the correct signature, or were inaccessible. Skipping");
        public void ReportInvalidOnPropertyNameChangedSignature(string name, string onChangedMethodName, IMethodSymbol method)
        {
            this.AddDiagnostic(invalidOnPropertyNameChangedSignature, method.Locations, onChangedMethodName, name);
        }

        private static readonly DiagnosticDescriptor multipleIsChangedProperties = CreateDescriptor(
            "INPC014",
            "Multiple [IsChanged] proeprties",
            "Found multiple [IsChanged] properties, but only one is allowed. Ignoring this one, and using '{0}'");
        public void ReportMultipleIsChangedProperties(string usedPropertyName, AttributeData attribute, ISymbol member)
        {
            this.AddDiagnostic(multipleIsChangedProperties, AttributeLocations(attribute, member), usedPropertyName);
        }

        private static readonly DiagnosticDescriptor nonBooleanIsChangedProperty = CreateDescriptor(
            "INPC015",
            "[IsChanged] property does not return bool",
            "[IsChanged] property '{0}' does not return a bool. Skipping");

        public void ReportNonBooleanIsChangedProperty(ISymbol member)
        {
            this.AddDiagnostic(nonBooleanIsChangedProperty, member.Locations, member.Name);
        }

        private static readonly DiagnosticDescriptor isChangedDoesNotHaveSetter = CreateDescriptor(
            "INPC016",
            "[IsChanged] property does not have a setter",
            "[IsChanged] property '{0}' does not have a setter. Skipping");

        public void ReportIsChangedDoesNotHaveSetter(ISymbol member)
        {
            this.AddDiagnostic(isChangedDoesNotHaveSetter, member.Locations, member.Name);
        }

        private static readonly DiagnosticDescriptor unknownFirstLetterCapitalisation = CreateDescriptor(
            "INPC017",
            "Unrecognised config value for propertychanged_first_letter_capitalization",
            "Unrecognised value '{0}' for config value propertychanged_first_letter_capitalization. Expected 'upper_case', 'lower_case' or 'none'");
        public void ReportUnknownFirstLetterCapitalisation(string firstLetterCapitalisation)
        {
            this.AddDiagnostic(unknownFirstLetterCapitalisation, (Location?)null, firstLetterCapitalisation);
        }

        private static readonly DiagnosticDescriptor readonlyBackingMember = CreateDescriptor(
            "INPC018",
            "Backing field cannot be readonly",
            "Backing field '{0}' cannot be readonly. Skipping");
        public void RaiseReadonlyBackingMember(IFieldSymbol field)
        {
            this.AddDiagnostic(readonlyBackingMember, field.Locations, field.Name);
        }

        private static readonly DiagnosticDescriptor backingPropertyMustHaveGetterAndSetter = CreateDescriptor(
            "INPC019",
            "Backing property must have a getter and a setter",
            "Backing property '{0}' must have a getter and a setter. Skipping");
        public void RaiseBackingPropertyMustHaveGetterAndSetter(IPropertySymbol property)
        {
            this.AddDiagnostic(backingPropertyMustHaveGetterAndSetter, property.Locations, property.Name);
        }

        private static readonly DiagnosticDescriptor outerTypeIsNotPartial = CreateDescriptor(
            "INPC020",
            "Outer type is not partial",
            "Type '{0}' must be partial in order for PropertyChanged.SourceGenerator to generate properties for inner type '{1}'");
        public void ReportOuterTypeIsNotPartial(INamedTypeSymbol outerType, INamedTypeSymbol innerType)
        {
            this.AddDiagnostic(outerTypeIsNotPartial, outerType.Locations, outerType.Name, innerType.Name);
        }

        private static DiagnosticDescriptor CreateDescriptor(string code, string title, string messageFormat, DiagnosticSeverity severity = DiagnosticSeverity.Warning)
        {
            string[] tags = severity == DiagnosticSeverity.Error ? new[] { WellKnownDiagnosticTags.NotConfigurable } : Array.Empty<string>();
            return new DiagnosticDescriptor(code, title, messageFormat, "PropertyChanged.SourceGenerator.Generation", severity, isEnabledByDefault: true, customTags: tags);
        }

        private void AddDiagnostic(DiagnosticDescriptor descriptor, IEnumerable<Location> locations, params object?[] args)
        {
            var locationsList = (locations as IReadOnlyList<Location>) ?? locations.ToList();
            this.AddDiagnostic(Diagnostic.Create(descriptor, locationsList.Count == 0 ? Location.None : locationsList[0], locationsList.Skip(1), args));
        }

        private void AddDiagnostic(DiagnosticDescriptor descriptor, Location? location = null, params object?[] args)
        {
            this.AddDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, args));
        }

        private void AddDiagnostic(Diagnostic diagnostic)
        {
            this.HasDiagnostics = true;
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                this.HasErrors = true;
            }
            this.Diagnostics.Add(diagnostic);
        }

        private static IEnumerable<Location> AttributeLocations(AttributeData? attributeData, ISymbol fallback)
        {
            // TODO: This squiggles the 'BasePath(...)' bit. Ideally we'd want '[BasePath(...)]' or perhaps just '...'.
            var attributeLocation = attributeData?.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            return attributeLocation != null ? new[] { attributeLocation } : fallback.Locations;
        }
    }
}
