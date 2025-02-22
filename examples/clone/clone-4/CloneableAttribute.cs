﻿using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.CodeFixes;
using Metalama.Framework.Diagnostics;
using Metalama.Framework.Project;

[Inheritable]
[EditorExperience( SuggestAsLiveTemplate = true )]
public class CloneableAttribute : TypeAspect
{
    private static readonly DiagnosticDefinition<(DeclarationKind, IFieldOrProperty)> _fieldOrPropertyCannotBeReadOnly =
        new("CLONE01", Severity.Error, "The {0} '{1}' cannot be read-only because it is marked as a [Child].");

    private static readonly DiagnosticDefinition<(DeclarationKind, IFieldOrProperty, IType)> _missingCloneMethod =
        new("CLONE02", Severity.Error,
            "The {0} '{1}' cannot be a [Child] because its type '{2}' does not have a 'Clone' parameterless method.");

    private static readonly DiagnosticDefinition<IProperty> _childPropertyMustBeAutomatic =
        new("CLONE03", Severity.Error, "The property '{0}' cannot be a [Child] because is not an automatic property.");

    private static readonly DiagnosticDefinition<(DeclarationKind, IFieldOrProperty)> _annotateFieldOrProperty =
        new("CLONE04", Severity.Warning, "Mark the {0} '{1}' as a [Child] or [Reference].");


    public override void BuildAspect( IAspectBuilder<INamedType> builder )
    {
        // Verify that child fields are valid.
        var hasError = false;
        foreach ( var fieldOrProperty in GetClonableFieldsOrProperties( builder.Target ) )
        {
            // The field or property must be writable.
            if ( fieldOrProperty.Writeability != Writeability.All )
            {
                builder.Diagnostics.Report(
                    _fieldOrPropertyCannotBeReadOnly.WithArguments( (fieldOrProperty.DeclarationKind,
                        fieldOrProperty) ), fieldOrProperty );
                hasError = true;
            }

            // If it is a field, it must be an automatic property.
            if ( fieldOrProperty is IProperty property && property.IsAutoPropertyOrField == false )
            {
                builder.Diagnostics.Report( _childPropertyMustBeAutomatic.WithArguments( property ), property );
                hasError = true;
            }

            // The type of the field must be cloneable.
            if ( !MetalamaExecutionContext.Current.ExecutionScenario.IsDesignTime )
            {
                var fieldType = fieldOrProperty.Type as INamedType;

                if ( fieldType == null ||
                     !(fieldType.AllMethods.OfName( "Clone" ).Where( p => p.Parameters.Count == 0 ).Any() ||
                       (fieldType.BelongsToCurrentProject &&
                        fieldType.Enhancements().HasAspect<CloneableAttribute>())) )
                {
                    builder.Diagnostics.Report(
                        _missingCloneMethod.WithArguments( (fieldOrProperty.DeclarationKind, fieldOrProperty,
                            fieldOrProperty.Type) ), fieldOrProperty );
                    hasError = true;
                }
            }
        }

        // Stop here if we have errors.
        if ( hasError )
        {
            builder.SkipAspect();
            return;
        }

        // Introduce the Clone method.
        builder.Advice.IntroduceMethod(
            builder.Target,
            nameof(this.CloneImpl),
            whenExists: OverrideStrategy.Override,
            args: new { T = builder.Target },
            buildMethod: m =>
            {
                m.Name = "Clone";
                m.ReturnType = builder.Target;
            } );
        builder.Advice.IntroduceMethod( /*<AddCloneMembers>*/
            builder.Target,
            nameof(this.CloneMembers),
            whenExists: OverrideStrategy.Override,
            args: new { T = builder.Target } ); /*</AddCloneMembers>*/

        // Implement the ICloneable interface.
        builder.Advice.ImplementInterface(
            builder.Target,
            typeof(ICloneable),
            OverrideStrategy.Ignore );

        // When we have non-child fields or properties of a cloneable type,
        // suggest to add the child attribute
        var eligibleChildren = builder.Target.FieldsAndProperties
            .Where( f => f.Writeability == Writeability.All &&
                         !f.IsImplicitlyDeclared &&
                         !f.Attributes.OfAttributeType( typeof(ChildAttribute) ).Any() &&
                         !f.Attributes.OfAttributeType( typeof(ReferenceAttribute) ).Any() &&
                         f.Type is INamedType fieldType &&
                         (fieldType.AllMethods.OfName( "Clone" ).Where( m => m.Parameters.Count == 0 ).Any() ||
                          fieldType.Attributes.OfAttributeType( typeof(CloneableAttribute) ).Any()) );


        foreach ( var fieldOrProperty in eligibleChildren ) /*<ReportUnannotatedProperties>*/
        {
            builder.Diagnostics.Report( _annotateFieldOrProperty
                .WithArguments( (fieldOrProperty.DeclarationKind, fieldOrProperty) ).WithCodeFixes(
                    CodeFixFactory.AddAttribute( fieldOrProperty, typeof(ChildAttribute), "Cloneable | Mark as child" ),
                    CodeFixFactory.AddAttribute( fieldOrProperty, typeof(ReferenceAttribute),
                        "Cloneable | Mark as reference" ) ), fieldOrProperty );
        } /*</ReportUnannotatedProperties>*/

        // If we don't have a CloneMember method, suggest to add it.
        if ( !builder.Target.Methods.OfName( nameof(this.CloneMembers) ).Any() ) /*<SuggestCloneMembers>*/
        {
            builder.Diagnostics.Suggest(
                new CodeFix( "Cloneable | Customize manually",
                    codeFix => codeFix.ApplyAspectAsync( builder.Target, new AddEmptyCloneMembersAspect() ) ) );
        } /*</SuggestCloneMembers>*/
    }

    private static IEnumerable<IFieldOrProperty> GetClonableFieldsOrProperties( INamedType type )
        => type.FieldsAndProperties.Where( f => f.Attributes.OfAttributeType( typeof(ChildAttribute) ).Any() );

    [Template]
    public virtual T CloneImpl<[CompileTime] T>()
    {
        // This compile-time variable will receive the expression representing the base call.
        // If we have a public Clone method, we will use it (this is the chaining pattern). Otherwise,
        // we will call MemberwiseClone (this is the initialization of the pattern).
        IExpression baseCall;

        if ( meta.Target.Method.IsOverride )
        {
            baseCall = (IExpression) meta.Base.Clone();
        }
        else
        {
            baseCall = (IExpression) meta.This.MemberwiseClone();
        }

        // Define a local variable of the same type as the target type.
        var clone = (T) baseCall.Value!;

        // Call CloneMembers, which may have a hand-written part.
        meta.This.CloneMembers( clone );


        return clone;
    }

    [Template]
    private void CloneMembers<[CompileTime] T>( T clone )
    {
        // Select clonable fields.
        var clonableFields = GetClonableFieldsOrProperties( meta.Target.Type );

        foreach ( var field in clonableFields )
        {
            // Check if we have a public method 'Clone()' for the type of the field.
            var fieldType = (INamedType) field.Type;

            field.With( clone ).Value = meta.Cast( fieldType, field.Value?.Clone() );
        }

        // Call the hand-written implementation, if any.
        meta.Proceed();
    }

    [InterfaceMember( IsExplicit = true )]
    private object Clone() => meta.This.Clone();
}