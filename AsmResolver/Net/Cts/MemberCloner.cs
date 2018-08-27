﻿using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.Net.Cil;
using AsmResolver.Net.Signatures;

namespace AsmResolver.Net.Cts
{
    public class MemberCloner
    {
        private sealed class MemberClonerReferenceImporter : ReferenceImporter
        {
            private readonly MemberCloner _memberCloner;

            public MemberClonerReferenceImporter(MemberCloner memberCloner, MetadataImage image) 
                : base(image)
            {
                _memberCloner = memberCloner;
            }

            public override ITypeDefOrRef ImportType(ITypeDefOrRef reference)
            {
                IMemberReference newType;
                return _memberCloner._createdMembers.TryGetValue(reference , out newType)
                    ? (ITypeDefOrRef) newType
                    : base.ImportType(reference);
            }

            public override ITypeDefOrRef ImportType(TypeDefinition type)
            {
                IMemberReference newType;
                return _memberCloner._createdMembers.TryGetValue(type, out newType)
                    ? (ITypeDefOrRef) newType
                    : base.ImportType(type);
            }

            public override IMethodDefOrRef ImportMethod(MethodDefinition method)
            {
                IMemberReference newMember;
                return _memberCloner._createdMembers.TryGetValue(method, out newMember)
                    ? (IMethodDefOrRef) newMember
                    : base.ImportMethod(method);
            }

            public override IMemberReference ImportField(FieldDefinition field)
            {
                IMemberReference newMember;
                return _memberCloner._createdMembers.TryGetValue(field, out newMember)
                    ? newMember
                    : base.ImportField(field);
            }

            public override MemberReference ImportMember(MemberReference reference)
            {
                IMemberReference newMember;
                return _memberCloner._createdMembers.TryGetValue(reference, out newMember)
                    ? (MemberReference) newMember
                    : base.ImportMember(reference);
            }

            public override IMemberReference ImportReference(IMemberReference reference)
            {
                IMemberReference newMember;
                return _memberCloner._createdMembers.TryGetValue(reference, out newMember)
                    ? newMember
                    : base.ImportReference(reference);
            }
        }

        private readonly MetadataImage _targetImage;
        private readonly IDictionary<IMemberReference, IMemberReference> _createdMembers =
            new Dictionary<IMemberReference, IMemberReference>(new SignatureComparer());
        private readonly IReferenceImporter _importer;

        public MemberCloner(MetadataImage targetImage)
        {
            if (targetImage == null)
                throw new ArgumentNullException("targetImage");
            _targetImage = targetImage;
            _importer = new MemberClonerReferenceImporter(this, targetImage);
        }

        public IList<TypeDefinition> CloneTypes(IList<TypeDefinition> types)
        {
            var newTypes = types.Select(CreateTypeStub).ToArray();
            
            for (var index = 0; index < newTypes.Length; index++)
                DeclareMemberStubs(types[index], newTypes[index]);
            
            for (var index = 0; index < newTypes.Length; index++)
                FinalizeTypeStub(types[index], newTypes[index]);

            return newTypes;
        }
        
        public TypeDefinition CloneType(TypeDefinition type)
        {
            var newType = CreateTypeStub(type);
            DeclareMemberStubs(type, newType);
            FinalizeTypeStub(type, newType);
            return newType;
        }

        private FieldDefinition CloneField(FieldDefinition field)
        {
            var newField = new FieldDefinition(field.Name, field.Attributes, _importer.ImportFieldSignature(field.Signature));

            if (newField.Constant != null)
                newField.Constant = new Constant(field.Constant.ConstantType, new DataBlobSignature(field.Constant.Value.Data));
            
            if (newField.PInvokeMap != null)
                newField.PInvokeMap = CloneImplementationMap(field.PInvokeMap);

            _createdMembers.Add(field, newField);
            CloneCustomAttributes(field, newField);
            
            return newField;
        }

        private TypeDefinition CreateTypeStub(TypeDefinition type)
        {
            var newType = new TypeDefinition(type.Namespace, type.Name, type.Attributes);
            _createdMembers.Add(type, newType);
            
            foreach (var nestedType in type.NestedClasses)
                newType.NestedClasses.Add(new NestedClass(CreateTypeStub(nestedType.Class)));

            return newType;
        }

        private void DeclareMemberStubs(TypeDefinition type, TypeDefinition stub)
        {
            foreach (var nestedType in type.NestedClasses)
                DeclareMemberStubs(nestedType.Class, (TypeDefinition) _createdMembers[nestedType.Class]);
            
            foreach (var field in type.Fields)
                stub.Fields.Add(CloneField(field));
            foreach (var method in type.Methods)
                stub.Methods.Add(CreateMethodStub(method));
            
            if (type.PropertyMap != null)
            {
                stub.PropertyMap = new PropertyMap();
                foreach (var property in type.PropertyMap.Properties)
                    stub.PropertyMap.Properties.Add(CloneProperty(property));
            }
            
            if (type.EventMap != null)
            {
                stub.EventMap = new EventMap();
                foreach (var @event in type.EventMap.Events)
                    stub.EventMap.Events.Add(CloneEvent(@event));
            }
        }

        private void FinalizeTypeStub(TypeDefinition type, TypeDefinition stub)
        {
            foreach (var nestedType in type.NestedClasses)
                FinalizeTypeStub(nestedType.Class, (TypeDefinition) _createdMembers[nestedType.Class]);
            
            foreach (var method in type.Methods)
                FinalizeMethod(method, (MethodDefinition) _createdMembers[method]);
            
            if (type.BaseType != null)
                stub.BaseType = _importer.ImportType(type.BaseType);

            if (type.ClassLayout != null)
                stub.ClassLayout = new ClassLayout(type.ClassLayout.ClassSize, type.ClassLayout.PackingSize);

            CloneGenericParameters(type, stub);
            CloneCustomAttributes(type, stub);
        }

        private EventDefinition CloneEvent(EventDefinition @event)
        {
            var newEvent = new EventDefinition(
                @event.Name,
                _importer.ImportType(@event.EventType));

            CloneSemantics(@event, newEvent);
            CloneCustomAttributes(@event, newEvent);
            
            return newEvent;
        }

        private PropertyDefinition CloneProperty(PropertyDefinition property)
        {
            var newProperty = new PropertyDefinition(
                property.Name,
                _importer.ImportPropertySignature(property.Signature));

            CloneSemantics(property, newProperty);
            CloneCustomAttributes(property, newProperty);
            
            return newProperty;
        }

        private void CloneSemantics(IHasSemantics owner, IHasSemantics newOwner)
        {
            foreach (var semantic in owner.Semantics)
            {
                newOwner.Semantics.Add(new MethodSemantics(
                    (MethodDefinition) _createdMembers[semantic.Method],
                    semantic.Attributes));
            }
        }

        private MethodDefinition CreateMethodStub(MethodDefinition method)
        {
            var newMethod = new MethodDefinition(
                method.Name,
                method.Attributes,
                _importer.ImportMethodSignature(method.Signature));

            foreach (var parameter in method.Parameters)
                newMethod.Parameters.Add(new ParameterDefinition(parameter.Sequence, parameter.Name, parameter.Attributes));

            if (method.PInvokeMap != null)
                newMethod.PInvokeMap = CloneImplementationMap(method.PInvokeMap);
            
            _createdMembers.Add(method, newMethod);
            return newMethod;
        }

        private void FinalizeMethod(MethodDefinition method, MethodDefinition stub)
        {
            CloneGenericParameters(method, stub);
            CloneCustomAttributes(method, stub);
            if (method.CilMethodBody != null)
                stub.CilMethodBody = CloneCilMethodBody(method.CilMethodBody, stub);
        }

        private ImplementationMap CloneImplementationMap(ImplementationMap map)
        {
            return new ImplementationMap(_importer.ImportModule(map.ImportScope), map.ImportName, map.Attributes);
        }
        
        private void CloneGenericParameters(IGenericParameterProvider source, IGenericParameterProvider newOwner)
        {
            foreach (var parameter in source.GenericParameters)
            {
                var newParameter = new GenericParameter(parameter.Index, parameter.Name, parameter.Attributes);
                foreach (var constraint in newParameter.Constraints)
                {
                    newParameter.Constraints.Add(new GenericParameterConstraint(
                        _importer.ImportType(constraint.Constraint)));
                }
                newOwner.GenericParameters.Add(newParameter);
            }
        }

        private void CloneCustomAttributes(IHasCustomAttribute source, IHasCustomAttribute newOwner)
        {
            foreach (var attribute in source.CustomAttributes)
            {
                var signature = new CustomAttributeSignature();
                
                foreach (var argument in attribute.Signature.FixedArguments)
                    signature.FixedArguments.Add(CloneAttributeArgument(argument));
                
                foreach (var argument in attribute.Signature.NamedArguments)
                {
                    signature.NamedArguments.Add(new CustomAttributeNamedArgument(argument.ArgumentMemberType,
                        _importer.ImportTypeSignature(argument.ArgumentType), argument.MemberName,
                        CloneAttributeArgument(argument.Argument)));
                }

                var newAttribute = new CustomAttribute((ICustomAttributeType) _importer.ImportReference(attribute.Constructor), signature);
                newOwner.CustomAttributes.Add(newAttribute);
            }
        }

        private CustomAttributeArgument CloneAttributeArgument(CustomAttributeArgument argument)
        {
            var newArgument = new CustomAttributeArgument(_importer.ImportTypeSignature(argument.ArgumentType));
            foreach (var element in argument.Elements)
                newArgument.Elements.Add(new ElementSignature(element.Value));
            return newArgument;
        }

        private CilMethodBody CloneCilMethodBody(CilMethodBody body, MethodDefinition newOwner)
        {
            var newBody = new CilMethodBody(newOwner)
            {
                InitLocals = body.InitLocals,
                MaxStack = body.MaxStack,
            };

            if (body.Signature != null)
                newBody.Signature = _importer.ImportStandAloneSignature(body.Signature);

            CloneInstructions(body, newBody);
            CloneExceptionHandlers(body, newBody);

            return newBody;
        }

        private void CloneInstructions(CilMethodBody body, CilMethodBody newBody)
        {
            var branchInstructions = new List<CilInstruction>();
            var switchInstructions = new List<CilInstruction>();
            foreach (var instruction in body.Instructions)
            {
                object operand = instruction.Operand;
                
                if (operand is IMemberReference)
                    operand = _importer.ImportReference((IMemberReference) operand);

                if (operand is VariableSignature)
                {
                    operand = ((LocalVariableSignature) newBody.Signature.Signature).Variables[
                        ((LocalVariableSignature) body.Signature.Signature).Variables.IndexOf(
                            (VariableSignature) operand)];
                }

                if (operand is ParameterSignature)
                {
                    operand = newBody.Method.Signature.Parameters[
                        body.Method.Signature.Parameters.IndexOf((ParameterSignature) operand)];
                }

                var newInstruction = new CilInstruction(instruction.Offset, instruction.OpCode, operand);
                newBody.Instructions.Add(newInstruction);
                switch (instruction.OpCode.OperandType)
                {
                    case CilOperandType.InlineBrTarget:
                    case CilOperandType.ShortInlineBrTarget:
                        branchInstructions.Add(newInstruction);
                        break;
                    case CilOperandType.InlineSwitch:
                        switchInstructions.Add(newInstruction);
                        break;
                }
            }

            foreach (var branch in branchInstructions)
                branch.Operand = newBody.Instructions.GetByOffset(((CilInstruction) branch.Operand).Offset);

            foreach (var @switch in switchInstructions)
            {
                var targets = (IEnumerable<CilInstruction>) @switch.Operand;
                var newTargets = new List<CilInstruction>();
                foreach (var target in targets)
                    newTargets.Add(newBody.Instructions.GetByOffset(target.Offset));
                @switch.Operand = newTargets;
            }
        }

        private void CloneExceptionHandlers(CilMethodBody body, CilMethodBody newBody)
        {
            foreach (var handler in body.ExceptionHandlers)
            {
                var newHandler = new ExceptionHandler(handler.HandlerType)
                {
                    TryStart = newBody.Instructions.GetByOffset(handler.TryStart.Offset),
                    TryEnd = newBody.Instructions.GetByOffset(handler.TryEnd.Offset),
                    HandlerStart = newBody.Instructions.GetByOffset(handler.HandlerStart.Offset),
                    HandlerEnd = newBody.Instructions.GetByOffset(handler.HandlerEnd.Offset),
                };
                
                if (handler.FilterStart != null)
                    newHandler.FilterStart = newBody.Instructions.GetByOffset(handler.FilterStart.Offset);
                if (handler.CatchType != null)
                    newHandler.CatchType = _importer.ImportType(handler.CatchType);
                
                newBody.ExceptionHandlers.Add(newHandler);
            }
        }
    }
}