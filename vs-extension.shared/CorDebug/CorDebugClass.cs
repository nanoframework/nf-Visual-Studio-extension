//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugClass : ICorDebugClass, ICorDebugClass2
    {
        CorDebugAssembly m_assembly;
        Class m_pdbxClass;
        TypeSpec m_pdbxTypeSpec;
        uint m_tkSymbolless;

        public CorDebugClass(CorDebugAssembly assembly, TypeSpec typeSpec)
        {
            m_assembly = assembly;
            m_pdbxTypeSpec = typeSpec;
            m_pdbxClass = null;
        }

        public CorDebugClass(CorDebugAssembly assembly, Class cls)
        {
            m_assembly = assembly;
            m_pdbxClass = cls;
            m_pdbxTypeSpec = null;
        }

        public CorDebugClass(CorDebugAssembly assembly, uint tkSymbolless)
        {
            m_tkSymbolless = tkSymbolless;
            m_assembly = assembly;
            m_pdbxClass = null;
            m_pdbxTypeSpec = null;
        }

        public ICorDebugClass ICorDebugClass
        {
            get { return (ICorDebugClass)this; }
        }

        public ICorDebugClass2 ICorDebugClass2
        {
            get { return (ICorDebugClass2)this; }
        }

        public CorDebugAssembly Assembly
        {
            [DebuggerHidden]
            get { return m_assembly; }
        }

        public bool IsEnum
        {
            get
            {
                if (HasSymbols && m_pdbxClass != null)
                {
                    return m_pdbxClass.IsEnum;
                }
                else
                {
                    return false;
                }
            }
        }

        public Engine Engine
        {
            [DebuggerHidden]
            get { return Process.Engine; }
        }

        public CorDebugProcess Process
        {
            [DebuggerHidden]
            get { return Assembly.Process; }
        }

        public CorDebugAppDomain AppDomain
        {
            [DebuggerHidden]
            get { return Assembly.AppDomain; }
        }

        public Class PdbxClass
        {
            [DebuggerHidden]
            get { return m_pdbxClass; }
        }

        public TypeSpec PdbxTypeSpec
        {
            [DebuggerHidden]
            get { return m_pdbxTypeSpec; }
        }

        public bool HasSymbols
        {
            get { return (m_pdbxClass != null || m_pdbxTypeSpec != null); }
        }

        public uint TypeDef_Index
        {
            get
            {
                uint tk = HasSymbols ? m_pdbxClass.Token.NanoCLRToken : m_tkSymbolless;

                return nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken(tk, Assembly);
            }
        }

        public uint TypeSpec_Index
        {
            get
            {
                uint tk = HasSymbols ? m_pdbxTypeSpec.Token.NanoCLRToken : m_tkSymbolless;

                return nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken(tk, Assembly);
            }
        }

        #region ICorDebugClass Members

        int ICorDebugClass.GetModule(out ICorDebugModule pModule)
        {
            pModule = m_assembly;

            return COM_HResults.S_OK;
        }

        int ICorDebugClass.GetToken(out uint pTypeDef)
        {
            pTypeDef = HasSymbols ? m_pdbxClass.Token.CLRToken : m_tkSymbolless;

            return COM_HResults.S_OK;
        }

        int ICorDebugClass.GetStaticFieldValue(uint fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue)
        {
            //Cache, and invalidate when necessary???
            uint fd = nanoCLR_TypeSystem.ClassMemberIndexFromCLRToken(fieldDef, Assembly);
            Process.SetCurrentAppDomain(AppDomain);

            RuntimeValue rtv = Engine.GetStaticFieldValue(fd);
            ppValue = CorDebugValue.CreateValue(rtv, AppDomain);

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugClass2 Members

        int ICorDebugClass2.GetParameterizedType(CorElementType elementType, uint nTypeArgs, ICorDebugType[] ppTypeArgs, out ICorDebugType ppType)
        {
            ppType = new CorDebugGenericType(elementType, null, Assembly);

            return COM_HResults.S_OK;
        }

        int ICorDebugClass2.SetJMCStatus(int bIsJustMyCode)
        {
            bool fJMC = Boolean.IntToBool(bIsJustMyCode);

            Debug.Assert(Utility.FImplies(fJMC, HasSymbols));

            int hres = fJMC ? COM_HResults.E_FAIL : COM_HResults.S_OK;

            if (HasSymbols)
            {
                if (Engine.Info_SetJMC(fJMC, ReflectionDefinition.Kind.REFLECTION_TYPE, TypeDef_Index))
                {
                    if (!m_assembly.IsFrameworkAssembly)
                    {
                        //now update the debugger JMC state...
                        foreach (Method m in m_pdbxClass.Methods)
                        {
                            m.IsJMC = fJMC;
                        }
                    }

                    hres = COM_HResults.S_OK;
                }
            }

            return hres;
        }

        #endregion
    }
}
