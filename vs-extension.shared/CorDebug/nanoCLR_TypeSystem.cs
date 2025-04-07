//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    //Lots of redefinitions from nanoCLR_Types.h
    public class nanoCLR_TypeSystem
    {
        //////////////////////////////////////////////////////////////////////////////////////
        // !!! KEEP IN SYNC WITH enum NanoCLRTable (in nanoCLR_TypeSystem VS extension) !!! //
        // !!! KEEP IN SYNC WITH enum NanoCLRTable (in nanoCLRT_Types.h in CLR)         !!! //
        //////////////////////////////////////////////////////////////////////////////////////

        public enum NanoCLRTable : uint
        {
            TBL_AssemblyRef = 0x00000000,
            TBL_TypeRef = 0x00000001,
            TBL_FieldRef = 0x00000002,
            TBL_MethodRef = 0x00000003,
            TBL_TypeDef = 0x00000004,
            TBL_FieldDef = 0x00000005,
            TBL_MethodDef = 0x00000006,
            TBL_GenericParam = 0x00000007,
            TBL_MethodSpec = 0x00000008,
            TBL_TypeSpec = 0x00000009,
            TBL_Attributes = 0x0000000A,
            TBL_Resources = 0x0000000B,
            TBL_ResourcesData = 0x0000000C,
            TBL_Strings = 0x0000000D,
            TBL_Signatures = 0x0000000E,
            TBL_ByteCode = 0x0000000F,
            TBL_ResourcesFiles = 0x00000010,
            TBL_EndOfAssembly = 0x000000011,
            TBL_Max = 0x00000012,
        };

        public enum CorTokenType : uint
        {
            mdtModule = 0x00000000,
            mdtTypeRef = 0x01000000,
            mdtTypeDef = 0x02000000,
            mdtFieldDef = 0x04000000,
            mdtMethodDef = 0x06000000,
            mdtParamDef = 0x08000000,
            mdtInterfaceImpl = 0x09000000,
            mdtMemberRef = 0x0a000000,
            mdtCustomAttribute = 0x0c000000,
            mdtPermission = 0x0e000000,
            mdtSignature = 0x11000000,
            mdtEvent = 0x14000000,
            mdtProperty = 0x17000000,
            mdtModuleRef = 0x1a000000,
            mdtTypeSpec = 0x1b000000,
            mdtAssembly = 0x20000000,
            mdtAssemblyRef = 0x23000000,
            mdtFile = 0x26000000,
            mdtExportedType = 0x27000000,
            mdtManifestResource = 0x28000000,
            mdtGenericParam = 0x2A000000,
            mdtString = 0x70000000,
            mdtName = 0x71000000,
            mdtBaseType = 0x72000000,       // Leave this on the high end value. This does not correspond to metadata table
        };

        public static uint IdxAssemblyFromIndex(uint idx)
        {
            return idx >> 16;
        }

        public static uint IdxFromIndex(uint idx)
        {
            return idx & 0xffff;
        }

        public static uint IndexFromIdxAssemblyIdx(uint idxAssm, uint idx)
        {
            return idxAssm << 16 | idx;
        }

        public static uint IndexFromIdxAssemblyIdx(uint idxAssm)
        {
            return idxAssm << 16;
        }

        public static NanoCLRTable CLR_TypeFromTk(uint tk)
        {
            return (NanoCLRTable)(tk >> 24);
        }

        public static uint CLR_DataFromTk(uint tk)
        {
            return tk & 0x00FFFFFF;
        }

        public static uint CLR_TkFromType(NanoCLRTable tbl, uint data)
        {
            return ((((uint)tbl) << 24) & 0xFF000000) | (data & 0x00FFFFFF);
        }

        public static CorDebugAssembly AssemblyFromIndex(CorDebugAppDomain appDomain, uint index)
        {
            return appDomain.AssemblyFromIdx(IdxAssemblyFromIndex(index));
        }

        public static CorDebugClass CorDebugClassFromTypeIndex(uint typeIndex, CorDebugAppDomain appDomain)
        {
            CorDebugClass cls = null;

            CorDebugAssembly assembly = appDomain.AssemblyFromIdx(IdxAssemblyFromIndex(typeIndex));
            if (assembly != null)
            {
                uint typedef = CLR_TkFromType(NanoCLRTable.TBL_TypeDef, IdxFromIndex(typeIndex));
                cls = assembly.GetClassFromNanoCLRToken(typedef);
            }

            return cls;
        }

        internal static CorDebugClass CorDebugClassFromTypeSpec(uint typeSpecIndex, CorDebugAppDomain appDomain)
        {
            CorDebugClass cls = null;

            CorDebugAssembly assembly = appDomain.AssemblyFromIdx(IdxAssemblyFromIndex(typeSpecIndex));
            if (assembly != null)
            {
                uint typeSpec = CLR_TkFromType(NanoCLRTable.TBL_TypeSpec, IdxFromIndex(typeSpecIndex));

                cls = assembly.GetClassFromNanoCLRToken(typeSpec);
            }

            return cls;
        }

        public static CorDebugFunction CorDebugFunctionFromMethodIndex(uint methodIndex, CorDebugAppDomain appDomain)
        {
            CorDebugFunction function = null;
            CorDebugAssembly assembly = appDomain.AssemblyFromIdx(IdxAssemblyFromIndex(methodIndex));

            if (assembly != null)
            {
                uint tk = NanoCLRTokenFromMethodIndex(methodIndex);
                function = assembly.GetFunctionFromTokennanoCLR(tk);
            }

            return function;
        }

        public static uint ClassMemberIndexFromCLRToken(uint token, CorDebugAssembly assembly)
        {
            ClassMember cm = assembly.GetPdbxClassMemberFromTokenCLR(token);

            return ClassMemberIndexFromnanoCLRToken(cm.NanoCLRToken, assembly);
        }

        public static uint ClassMemberIndexFromnanoCLRToken(uint token, CorDebugAssembly assembly)
        {
            uint idx = CLR_DataFromTk(token);
            return IndexFromIdxAssemblyIdx(assembly.Idx, idx);
        }

        private static uint NanoCLRTokenFromIndex(nanoCLR_TypeSystem.NanoCLRTable tbl, uint index)
        {
            uint idxAssembly = IdxAssemblyFromIndex(index);
            uint idxMethod = IdxFromIndex(index);

            return CLR_TkFromType(tbl, idxMethod);
        }

        public static uint NanoCLRTokenFromMethodIndex(uint index)
        {
            return NanoCLRTokenFromIndex(NanoCLRTable.TBL_MethodDef, index);
        }

        public static uint NanoCLRTokenFromTypeIndex(uint index)
        {
            return NanoCLRTokenFromIndex(NanoCLRTable.TBL_TypeDef, index);
        }

        public class SymbollessSupport
        {
            public static uint MethodDefTokenFromNanoCLRToken(uint token)
            {
                Debug.Assert(CLR_TypeFromTk(token) == NanoCLRTable.TBL_MethodDef);
                return (uint)CorTokenType.mdtMethodDef | CLR_DataFromTk(token);
            }

            public static uint NanoCLRTokenFromMethodDefToken(uint token)
            {
                Debug.Assert((token & (uint)CorTokenType.mdtMethodDef) != 0);
                return CLR_TkFromType(NanoCLRTable.TBL_MethodDef, token & 0x00ffffff);
            }

            public static uint TypeDefTokenFromNanoCLRToken(uint token)
            {
                Debug.Assert(CLR_TypeFromTk(token) == NanoCLRTable.TBL_TypeDef);
                return (uint)CorTokenType.mdtTypeDef | CLR_DataFromTk(token);
            }

            public static uint NanoCLRTokenFromTypeDefToken(uint token)
            {
                Debug.Assert((token & (uint)CorTokenType.mdtTypeDef) != 0);
                return CLR_TkFromType(NanoCLRTable.TBL_TypeDef, token & 0x00ffffff);
            }

            internal static uint TypeSpecTokenFromNanoCLRToken(uint token)
            {
                Debug.Assert(CLR_TypeFromTk(token) == NanoCLRTable.TBL_TypeSpec);
                return (uint)CorTokenType.mdtTypeSpec | CLR_DataFromTk(token);
            }

            public static uint NanoCLRTokenFromTypeSpecToken(uint token)
            {
                Debug.Assert((token & (uint)CorTokenType.mdtTypeSpec) != 0);
                return CLR_TkFromType(NanoCLRTable.TBL_TypeSpec, token & 0x00ffffff);
            }

        }
    }
}
