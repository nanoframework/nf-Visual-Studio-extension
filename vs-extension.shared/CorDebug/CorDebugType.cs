//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /// <summary>
    /// Summary description for CorDebugType.
    /// </summary>
    public class CorDebugTypeArray : ICorDebugType
    {
        CorDebugValueArray m_ValueArray;

        public CorDebugTypeArray(CorDebugValueArray valArray)
        {
            m_ValueArray = valArray;
        }

        int ICorDebugType.EnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
        {
            ppTyParEnum = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetType(out CorElementType ty)
        {
            // This is for arrays. ELEMENT_TYPE_SZARRAY - means single demensional array.
            ty = CorElementType.ELEMENT_TYPE_SZARRAY;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetRank(out uint pnRank)
        {
            // ELEMENT_TYPE_SZARRAY - means single demensional array.
            pnRank = 1;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetClass(out ICorDebugClass ppClass)
        {
            ppClass = CorDebugValue.ClassFromRuntimeValue(m_ValueArray.RuntimeValue, m_ValueArray.AppDomain);
            return COM_HResults.S_OK;
        }

        /*
         *  The function ICorDebugType.GetFirstTypeParameter returns the type 
         *  of element in the array.
         *  It control viewing of arrays elements in the watch window of debugger.
         */
        int ICorDebugType.GetFirstTypeParameter(out ICorDebugType value)
        {
            value = new CorDebugGenericType(CorElementType.ELEMENT_TYPE_CLASS, m_ValueArray.RuntimeValue, m_ValueArray.AppDomain);
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetStaticFieldValue(uint fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue)
        {
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetBase(out ICorDebugType pBase)
        {
            pBase = null;
            return COM_HResults.E_NOTIMPL;
        }
    }

    public class CorDebugGenericType : ICorDebugType
    {
        CorElementType m_elemType;
        public RuntimeValue m_rtv;
        public CorDebugAppDomain m_appDomain;

        public CorDebugAssembly Assembly
        {
            [System.Diagnostics.DebuggerHidden]
            get;
        }

        public Engine Engine
        {
            [System.Diagnostics.DebuggerHidden]
            get { return this.Process?.Engine; }
        }

        public CorDebugProcess Process
        {
            [System.Diagnostics.DebuggerHidden]
            get { return this.Assembly?.Process; }
        }

        public CorDebugAppDomain AppDomain
        {
            [System.Diagnostics.DebuggerHidden]
            get
            {
                if (m_appDomain != null)
                {
                    return m_appDomain;
                }
                else
                {
                    return this.Assembly?.AppDomain;
                }
            }
        }

        // This is used to resolve values into types when we know the appdomain, but not the assembly.
        public CorDebugGenericType(CorElementType elemType, RuntimeValue rtv, CorDebugAppDomain appDomain)
        {
            m_elemType = elemType;
            m_rtv = rtv;
            m_appDomain = appDomain;
        }

        // This constructor is used exclusively for resolving potentially (but never really) generic classes into fully specified types.
        // Generics are not supported (yet) but we still need to be able to convert classes into fully specified types.      
        public CorDebugGenericType(CorElementType elemType, RuntimeValue rtv, CorDebugAssembly assembly)
        {
            m_elemType = elemType;
            m_rtv = rtv;
            Assembly = assembly;
        }

        int ICorDebugType.EnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
        {
            // VS drives Microsoft's stock managed debug engine over this ICorDebug implementation, and its
            // expression evaluator builds the displayed name from GetClass (the open TypeDef, named through
            // IMetaDataImport) plus the arguments enumerated here. Supplying them is what turns Box`1 into
            // Box<int> in Locals/Watch/Autos.
            //
            // Every failure below falls back to E_NOTIMPL, which is exactly the behaviour that shipped
            // before, so a type we cannot describe still renders as the open type rather than breaking.
            ppTyParEnum = null;

            ICorDebugType[] typeParameters = CorDebugTypeParameter.FromRuntimeValue(m_rtv, AppDomain);

            if (typeParameters == null)
            {
                return COM_HResults.E_NOTIMPL;
            }

            ppTyParEnum = new CorDebugEnum(
                typeParameters,
                typeof(ICorDebugType),
                typeof(ICorDebugTypeEnum)) as ICorDebugTypeEnum;

            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetType(out CorElementType ty)
        {
            // Return CorElementType element type.
            ty = m_elemType;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetRank(out uint pnRank)
        {
            // Not an array. Thus rank is zero
            pnRank = 0;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetClass(out ICorDebugClass ppClass)
        {
            ppClass = CorDebugValue.ClassFromRuntimeValue(m_rtv, AppDomain);
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetFirstTypeParameter(out ICorDebugType value)
        {
            // For non-arrays there is not first parameter.
            value = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetStaticFieldValue(uint fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue)
        {
            uint fd = nanoCLR_TypeSystem.ClassMemberIndexFromCLRToken(fieldDef, this.Assembly);

            this.Process.SetCurrentAppDomain(this.AppDomain);
            RuntimeValue rtv = this.Engine.GetStaticFieldValue(fd);
            ppValue = CorDebugValue.CreateValue(rtv, this.AppDomain);

            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetBase(out ICorDebugType pBase)
        {
            pBase = null;
            return COM_HResults.E_NOTIMPL;
        }
    }

    /// <summary>
    /// One type argument of a closed generic instance, handed to the expression evaluator through
    /// <see cref="ICorDebugType.EnumerateTypeParameters"/>.
    /// </summary>
    /// <remarks>
    /// The CLR reports a generic instance as DATATYPE_CLASS/DATATYPE_VALUETYPE with HB_GenericInstance set,
    /// m_td pointing at the open TypeDef and m_ts at the closed TypeSpec. The arguments come from the
    /// structured <see cref="TypeSpec.GenericArguments"/> the metadata processor now writes for that
    /// TypeSpec -- each argument is either a primitive (<see cref="TypeSpecArg.PrimitiveType"/>, a
    /// <c>NanoCLRDataType</c> name) or a class/nested-TypeSpec addressed by NanoCLR token
    /// (<see cref="TypeSpecArg.TypeToken"/>), with the class's Cecil full name
    /// (<see cref="TypeSpecArg.ClassName"/>) as a fallback for classes declared outside the assembly
    /// that owns the TypeSpec. Resolution never touches nanoHelpers.FixTypeNames or any parsed display name.
    /// </remarks>
    public class CorDebugTypeParameter : ICorDebugType
    {
        private readonly CorElementType _elementType;
        private readonly CorDebugClass _class;

        private CorDebugTypeParameter(CorElementType elementType, CorDebugClass cls)
        {
            _elementType = elementType;
            _class = cls;
        }

        /// <summary>
        /// Maps every primitive <c>nanoClrDataType</c> the metadata processor can emit as a
        /// <see cref="TypeSpecArg.PrimitiveType"/> to the <see cref="CorElementType"/> the expression
        /// evaluator expects. Anything absent here (a non-primitive DATATYPE_* value) is never produced for
        /// a primitive argument, so it simply fails to resolve -- see <see cref="Resolve"/>.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<nanoClrDataType, CorElementType> _primitiveElementTypes =
            new System.Collections.Generic.Dictionary<nanoClrDataType, CorElementType>
            {
                { nanoClrDataType.DATATYPE_VOID, CorElementType.ELEMENT_TYPE_VOID },
                { nanoClrDataType.DATATYPE_BOOLEAN, CorElementType.ELEMENT_TYPE_BOOLEAN },
                { nanoClrDataType.DATATYPE_CHAR, CorElementType.ELEMENT_TYPE_CHAR },
                { nanoClrDataType.DATATYPE_I1, CorElementType.ELEMENT_TYPE_I1 },
                { nanoClrDataType.DATATYPE_U1, CorElementType.ELEMENT_TYPE_U1 },
                { nanoClrDataType.DATATYPE_I2, CorElementType.ELEMENT_TYPE_I2 },
                { nanoClrDataType.DATATYPE_U2, CorElementType.ELEMENT_TYPE_U2 },
                { nanoClrDataType.DATATYPE_I4, CorElementType.ELEMENT_TYPE_I4 },
                { nanoClrDataType.DATATYPE_U4, CorElementType.ELEMENT_TYPE_U4 },
                { nanoClrDataType.DATATYPE_I8, CorElementType.ELEMENT_TYPE_I8 },
                { nanoClrDataType.DATATYPE_U8, CorElementType.ELEMENT_TYPE_U8 },
                { nanoClrDataType.DATATYPE_R4, CorElementType.ELEMENT_TYPE_R4 },
                { nanoClrDataType.DATATYPE_R8, CorElementType.ELEMENT_TYPE_R8 },
                { nanoClrDataType.DATATYPE_STRING, CorElementType.ELEMENT_TYPE_STRING },
                { nanoClrDataType.DATATYPE_OBJECT, CorElementType.ELEMENT_TYPE_OBJECT },
            };

        /// <summary>
        /// Builds the type arguments for a runtime value that is a closed generic instance.
        /// </summary>
        /// <returns>
        /// The arguments, or <see langword="null"/> when the value is not a generic instance or when any
        /// argument cannot be resolved. Callers are expected to report E_NOTIMPL on <see langword="null"/>
        /// so that the type still renders as the open generic.
        /// </returns>
        internal static ICorDebugType[] FromRuntimeValue(RuntimeValue rtv, CorDebugAppDomain appDomain)
        {
            if (rtv == null || appDomain == null || !rtv.IsGenericInstance)
            {
                return null;
            }

            // m_ts: the closed TypeSpec that carries the structured argument list.
            CorDebugClass typeSpecClass = nanoCLR_TypeSystem.CorDebugClassFromTypeSpec(
                rtv.GenericTypeSpec,
                appDomain);

            TypeSpec typeSpec = typeSpecClass?.PdbxTypeSpec;

            if (typeSpec == null || !typeSpec.IsGenericInstance || typeSpec.GenericArguments == null)
            {
                return null;
            }

            // Class/nested-TypeSpec tokens on each argument are NanoCLR tokens local to the assembly that
            // owns this TypeSpec -- resolve them against that same assembly, not the appdomain at large.
            CorDebugAssembly owningAssembly = typeSpecClass.Assembly;

            ICorDebugType[] arguments = new ICorDebugType[typeSpec.GenericArguments.Count];

            for (int i = 0; i < arguments.Length; i++)
            {
                CorDebugTypeParameter argument = Resolve(typeSpec.GenericArguments[i], owningAssembly, appDomain);

                if (argument == null)
                {
                    // Partial answers are worse than none: the evaluator would render a name with holes in
                    // it. Give up on the whole instance and let it fall back to the open type.
                    return null;
                }

                arguments[i] = argument;
            }

            return arguments;
        }

        private static CorDebugTypeParameter Resolve(TypeSpecArg argument, CorDebugAssembly owningAssembly, CorDebugAppDomain appDomain)
        {
            if (argument.IsPrimitive)
            {
                if (!System.Enum.TryParse(argument.PrimitiveType, out nanoClrDataType dataType) ||
                    !_primitiveElementTypes.TryGetValue(dataType, out CorElementType elementType))
                {
                    return null;
                }

                return new CorDebugTypeParameter(elementType, null);
            }

            if (argument.IsGenericParameter)
            {
                // An open type parameter (VAR/MVAR), not a closed type -- e.g. the T in Pair<T,int>
                // inside the generic type that declares T. Nothing to resolve to a class; the whole
                // instance falls back to the open-type display, same as any other unresolved argument.
                return null;
            }

            // Prefer the token: it is unambiguous and, for a nested generic instance, is the only way to
            // resolve it at all (a closed generic instance has no Class/full-name entry of its own).
            CorDebugClass cls = null;

            if (argument.TypeToken != null && owningAssembly != null)
            {
                cls = owningAssembly.GetClassFromNanoCLRToken(argument.TypeToken.NanoCLRToken);
            }

            // Fall back to a cross-assembly by-name lookup for an ordinary class declared in a different
            // assembly than the one that owns this TypeSpec (no token was available for it -- see the
            // remarks on TypeSpecArg.TypeToken).
            if (cls == null && !string.IsNullOrEmpty(argument.ClassName) && appDomain != null)
            {
                cls = appDomain.ClassFromFullName(argument.ClassName);
            }

            if (cls == null)
            {
                return null;
            }

            return new CorDebugTypeParameter(
                cls.IsEnum ? CorElementType.ELEMENT_TYPE_VALUETYPE : CorElementType.ELEMENT_TYPE_CLASS,
                cls);
        }

        int ICorDebugType.GetType(out CorElementType ty)
        {
            ty = _elementType;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetClass(out ICorDebugClass ppClass)
        {
            ppClass = _class;

            return _class == null ? COM_HResults.E_FAIL : COM_HResults.S_OK;
        }

        int ICorDebugType.EnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum)
        {
            ppTyParEnum = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetFirstTypeParameter(out ICorDebugType value)
        {
            value = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetRank(out uint pnRank)
        {
            // Not an array.
            pnRank = 0;
            return COM_HResults.S_OK;
        }

        int ICorDebugType.GetStaticFieldValue(uint fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue)
        {
            ppValue = null;
            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugType.GetBase(out ICorDebugType pBase)
        {
            pBase = null;
            return COM_HResults.E_NOTIMPL;
        }
    }
}
