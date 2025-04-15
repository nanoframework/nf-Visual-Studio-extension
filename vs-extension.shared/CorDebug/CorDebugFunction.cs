//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System;
using System.Collections;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class CorDebugFunction : ICorDebugFunction, ICorDebugFunction2
    {
        CorDebugClass m_class;
        Pdbx.Method m_pdbxMethod;
        CorDebugCode m_codeNative;
        CorDebugCode m_codeIL;
        uint m_tkSymbolless;

        public CorDebugFunction(CorDebugClass cls, Pdbx.Method method)
        {
            m_class = cls;
            m_pdbxMethod = method;
        }

        public CorDebugFunction(CorDebugClass cls, uint tkSymbolless) : this(cls, null)
        {
            m_tkSymbolless = tkSymbolless;
        }

        public ICorDebugFunction ICorDebugFunction
        {
            get { return (ICorDebugFunction)this; }
        }

        public ICorDebugFunction2 ICorDebugFunction2
        {
            get { return (ICorDebugFunction2)this; }
        }

        public CorDebugClass Class
        {
            [DebuggerHidden]
            get { return m_class; }
        }

        public CorDebugAppDomain AppDomain
        {
            [DebuggerHidden]
            get { return Class.AppDomain; }
        }

        public CorDebugProcess Process
        {
            [DebuggerHidden]
            get { return Class.Process; }
        }

        public CorDebugAssembly Assembly
        {
            [DebuggerHidden]
            get { return Class.Assembly; }
        }

        private Engine Engine
        {
            [DebuggerHidden]
            get { return Class.Engine; }
        }

        [System.Diagnostics.DebuggerStepThrough]
        private CorDebugCode GetCode(ref CorDebugCode code)
        {
            if (code == null)
                code = new CorDebugCode(this);
            return code;
        }

        public bool HasSymbols
        {
            get { return m_pdbxMethod != null; }
        }

        public uint MethodDef_Index
        {
            get
            {
                uint tk = HasSymbols ? m_pdbxMethod.Token.nanoCLR : m_tkSymbolless;

                return nanoCLR_TypeSystem.ClassMemberIndexFromnanoCLRToken(tk, m_class.Assembly);
            }
        }

        public Pdbx.Method PdbxMethod
        {
            [DebuggerHidden]
            get { return m_pdbxMethod; }
        }

        public bool IsInternal
        {
            get { return MetaData.Helper.MethodIsInternal(Class.Assembly.MetaDataImport, m_pdbxMethod.Token.CLR); }
        }

        public bool IsInstance
        {
            get { return MetaData.Helper.MethodIsInstance(Class.Assembly.MetaDataImport, m_pdbxMethod.Token.CLR); }
        }

        public bool IsVirtual
        {
            get { return MetaData.Helper.MethodIsVirtual(Class.Assembly.MetaDataImport, m_pdbxMethod.Token.CLR); }
        }

        public uint NumArg
        {
            get { return MetaData.Helper.MethodGetNumArg(Class.Assembly.MetaDataImport, m_pdbxMethod.Token.CLR); }
        }

        public uint GetILCLRFromILnanoCLR(uint ilnanoCLR)
        {
            uint ilCLR;

            //Special case for CatchHandlerFound and AppDomain transitions; possibly used elsewhere.
            if (ilnanoCLR == uint.MaxValue) return uint.MaxValue;

            ilCLR = ILComparer.Map(false, m_pdbxMethod.ILMap, ilnanoCLR);
            Debug.Assert(ilnanoCLR <= ilCLR);

            return ilCLR;
        }

        public uint GetILnanoCLRFromILCLR(uint ilCLR)
        {
            //Special case for when CPDE wants to step to the end of the function?
            if (ilCLR == uint.MaxValue) return uint.MaxValue;

            uint ilnanoCLR = ILComparer.Map(true, m_pdbxMethod.ILMap, ilCLR);

            Debug.Assert(ilnanoCLR <= ilCLR);

            return ilnanoCLR;
        }

        private class ILComparer : IComparer
        {
            bool m_fCLR;

            private ILComparer(bool fCLR)
            {
                m_fCLR = fCLR;
            }

            private static uint GetIL(bool fCLR, Pdbx.IL il)
            {
                return fCLR ? il.CLR : il.nanoCLR;
            }

            private uint GetIL(Pdbx.IL il)
            {
                return GetIL(m_fCLR, il);
            }

            private static void SetIL(bool fCLR, Pdbx.IL il, uint offset)
            {
                if (fCLR)
                    il.CLR = offset;
                else
                    il.nanoCLR = offset;
            }

            private void SetIL(Pdbx.IL il, uint offset)
            {
                SetIL(m_fCLR, il, offset);
            }

            public int Compare(object o1, object o2)
            {
                return GetIL(o1 as Pdbx.IL).CompareTo(GetIL(o2 as Pdbx.IL));
            }

            public static uint Map(bool fCLR, Pdbx.IL[] ilMap, uint offset)
            {
                ILComparer ilComparer = new ILComparer(fCLR);
                Pdbx.IL il = new Pdbx.IL();
                ilComparer.SetIL(il, offset);
                int i = Array.BinarySearch(ilMap, il, ilComparer);
                uint ret = 0;

                if (i >= 0)
                {
                    //Exact match
                    ret = GetIL(!fCLR, ilMap[i]);
                }
                else
                {

                    i = ~i;

                    if (i == 0)
                    {
                        //Before the IL diverges
                        ret = offset;
                    }
                    else
                    {
                        //Somewhere in between
                        i--;

                        il = ilMap[i];
                        ret = offset - GetIL(fCLR, il) + GetIL(!fCLR, il);
                    }
                }

                Debug.Assert(ret >= 0);
                return ret;
            }
        }

        #region ICorDebugFunction Members

        int ICorDebugFunction.GetLocalVarSigToken(out uint pmdSig)
        {
            pmdSig = 0;

            return COM_HResults.E_NOTIMPL;
        }

        int ICorDebugFunction.CreateBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint)
        {
            ppBreakpoint = new CorDebugFunctionBreakpoint(this, 0);

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetILCode(out ICorDebugCode ppCode)
        {
            ppCode = GetCode(ref m_codeIL);

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetModule(out ICorDebugModule ppModule)
        {
            m_class.ICorDebugClass.GetModule(out ppModule);

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetNativeCode(out ICorDebugCode ppCode)
        {
            ppCode = GetCode(ref m_codeNative);

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetToken(out uint pMethodDef)
        {
            pMethodDef = HasSymbols ? m_pdbxMethod.Token.CLR : m_tkSymbolless;

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetClass(out ICorDebugClass ppClass)
        {
            ppClass = m_class;

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction.GetCurrentVersionNumber(out uint pnCurrentVersion)
        {
            pnCurrentVersion = 0;

            return COM_HResults.S_OK;
        }

        #endregion

        #region ICorDebugFunction2 Members

        int ICorDebugFunction2.SetJMCStatus(int bIsJustMyCode)
        {
            bool fJMC = Boolean.IntToBool(bIsJustMyCode);

            Debug.Assert(Utility.FImplies(fJMC, HasSymbols));

            int hres = fJMC ? COM_HResults.E_FAIL : COM_HResults.S_OK;

            if (HasSymbols)
            {
                if (fJMC != m_pdbxMethod.IsJMC && m_pdbxMethod.CanSetJMC)
                {
                    if (Engine.Info_SetJMC(fJMC, ReflectionDefinition.Kind.REFLECTION_METHOD, MethodDef_Index))
                    {
                        if (!Assembly.IsFrameworkAssembly)
                        {
                            //now update the debugger JMC state...
                            m_pdbxMethod.IsJMC = fJMC;
                        }

                        hres = COM_HResults.S_OK;
                    }
                }
            }

            return hres;
        }

        int ICorDebugFunction2.GetJMCStatus(out int pbIsJustMyCode)
        {
            pbIsJustMyCode = Boolean.BoolToInt(HasSymbols ? m_pdbxMethod.IsJMC : false);

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction2.GetVersionNumber(out uint pnVersion)
        {
            // CorDebugFunction.GetVersionNumber is not implemented
            pnVersion = 1;

            return COM_HResults.S_OK;
        }

        int ICorDebugFunction2.EnumerateNativeCode(out ICorDebugCodeEnum ppCodeEnum)
        {
            ppCodeEnum = null;

            return COM_HResults.S_OK;
        }

        #endregion        
    }
}
