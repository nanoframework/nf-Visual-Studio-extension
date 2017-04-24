//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

#include "stdafx.h"

////////////////////////////////////////////////////////////////////////////////////////////////////

HRESULT CLR_CompressTokenHelper(const CLR_TABLESENUM *tables, CLR_UINT16 cTables, CLR_UINT32& tk)
{
    CLR_TABLESENUM tbl    = CLR_TypeFromTk(tk);
    bool           fFound = false;
    CLR_UINT16     iTable;

    for(iTable = 0; iTable < cTables; iTable++)
    {
        if(tbl == tables[iTable])
        {
            fFound = true;
            break;
        }
    }

    if(!fFound) return CLR_E_FAIL;

    if(cTables < 1 || cTables > 3) return CLR_E_FAIL;

    int cBitsTable = cTables - 1;
    tk = (iTable << (16 - cBitsTable)) | (tk & (0xffff >> cBitsTable));

    return S_OK;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

HRESULT MetaData::ByteCode::ConvertTokens( mdTokenMap& lookupIDs )
{
    NANOCLR_HEADER();

    size_t len = m_opcodes.size();

    for(size_t i=0; i<len; i++)
    {
        LogicalOpcodeDesc& ref = m_opcodes[i];

        if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TOKEN)
        {
            if(!MetaData::IsTokenPresent( lookupIDs, ref.m_token ) || IsNilToken( ref.m_token ))
            {
                wprintf( L"Method: %s\n", m_name.c_str() );
                wprintf( L"Unknown token at %d: 0x%08X\n", i, ref.m_token );

                DumpStats();
                NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
            }

            ref.m_token = lookupIDs[ref.m_token];
        }
    }

    NANOCLR_NOCLEANUP();
}

HRESULT MetaData::ByteCode::GenerateOldIL( std::vector<BYTE>& code )
{
    NANOCLR_HEADER();

    CLR_UINT32 offset = 0;
    size_t     len    = m_opcodes.size();
    size_t     i;

    //
    // Compute length of opcodes.
    //
    for(i=0; i<len; i++)
    {
        LogicalOpcodeDesc& ref   = m_opcodes[i];
        CLR_UINT32         ipLen = ref.m_ipLength;

        if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TOKEN)
        {
            if(ref.m_op != CEE_LDTOKEN)
            {
                ipLen -= 2;
            }
        }

        if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TARGET)
        {
            if(ref.m_op == CEE_SWITCH)
            {
                if(ref.m_targets.size() > 0xFF) NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);

                ipLen -= 3;

                ipLen -= (CLR_UINT32)(ref.m_targets.size() * 2);
            }
            else if(ref.m_ol->m_opParam == CLR_OpcodeParam_BrTarget)
            {
                ipLen -= 2;
            }
        }

        ref.m_ipLength = ipLen;
        ref.m_ipOffset = offset; offset += ipLen;
    }

    code.resize( offset );

    //--//

    //
    // Fix branches.
    //
    for(i=0; i<len; i++)
    {
        LogicalOpcodeDesc& ref = m_opcodes[i];

        if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TARGET)
        {
            for(CLR_UINT32 j=0; j<ref.m_targets.size(); j++)
            {
                LogicalOpcodeDesc& refTarget = m_opcodes[ ref.m_targets[j] ];
                CLR_INT32          diff;

                diff = refTarget.m_ipOffset - (ref.m_ipOffset + ref.m_ipLength);

                if(diff < -0x8000 || diff > 0x7FFF) NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);

                ref.m_targets[j] = diff;
            }
        }
    }

    //--//

    BYTE* ip = &code[0];

    for(i=0; i<len; i++)
    {
        LogicalOpcodeDesc& ref     = m_opcodes[i];
        BYTE*              ipStart = ip;

        if(ref.m_op >= 256)
        {
            *ip++ = CEE_PREFIX1;
            *ip++ = ref.m_op - 256;
        }
        else
        {
            *ip++ = ref.m_op;
        }

        if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TOKEN)
        {
            CLR_UINT32 tk = ref.m_token;

            switch(ref.m_ol->m_opParam)
            {
            case CLR_OpcodeParam_Field : NANOCLR_CHECK_HRESULT( CLR_CompressFieldToken ( tk ) ); NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)tk ); break;
            case CLR_OpcodeParam_Method: NANOCLR_CHECK_HRESULT( CLR_CompressMethodToken( tk) ); NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)tk ); break;
            case CLR_OpcodeParam_String: NANOCLR_CHECK_HRESULT( CLR_CompressStringToken( tk) ); NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)tk ); break;
            case CLR_OpcodeParam_Type  : NANOCLR_CHECK_HRESULT( CLR_CompressTypeToken  ( tk) ); NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)tk ); break;
            default                    : 
                NANOCLR_WRITE_UNALIGNED_UINT32( ip, (CLR_UINT32)tk ); 
                break;
            }
        }
        else if(ref.m_ol->m_flags & CLR_RT_OpcodeLookup::ATTRIB_HAS_TARGET)
        {
            if(ref.m_op == CEE_SWITCH)
            {
                NANOCLR_WRITE_UNALIGNED_UINT8( ip, (CLR_UINT8)ref.m_targets.size() );

                for(CLR_UINT32 j=0; j<ref.m_targets.size(); j++)
                {
                    NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)ref.m_targets[j] );
                }
            }
            else if(ref.m_ol->m_opParam == CLR_OpcodeParam_BrTarget)
            {
                NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)ref.m_targets[0] );
            }
            else
            {
                NANOCLR_WRITE_UNALIGNED_UINT8( ip, (CLR_UINT8)ref.m_targets[0] );
            }
        }
        else
        {
            // FIXME GJS do these need to be endian swapped too??
            switch(ref.m_ol->m_opParam)
            {
                /*#define InlineField         4, 2, */ case CLR_OpcodeParam_Field           : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineMethod        4, 2, */ case CLR_OpcodeParam_Method          : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineType          4, 2, */ case CLR_OpcodeParam_Type            : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineString        4, 2, */ case CLR_OpcodeParam_String          : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineTok           4, 4, */ case CLR_OpcodeParam_Tok             : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineSig           4, 4, */ case CLR_OpcodeParam_Sig             : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineBrTarget      4, 2, */ case CLR_OpcodeParam_BrTarget        : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define ShortInlineBrTarget 1, 1, */ case CLR_OpcodeParam_ShortBrTarget   : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineI             4, 4, */ case CLR_OpcodeParam_I               : NANOCLR_WRITE_UNALIGNED_INT32 ( ip,             ref.m_arg_I4 ); break;
                /*#define InlineI8            8, 8, */ case CLR_OpcodeParam_I8              : NANOCLR_WRITE_UNALIGNED_INT64 ( ip,             ref.m_arg_I8 ); break;
                /*#define InlineNone          0, 0, */ case CLR_OpcodeParam_None            :                                                                 break;
                /*#define InlineR             8, 8, */ case CLR_OpcodeParam_R               : NANOCLR_WRITE_UNALIGNED_INT64 ( ip,             ref.m_arg_R8 ); break;
                /*#define InlineSwitch        4, 1, */ case CLR_OpcodeParam_Switch          : NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
                /*#define InlineVar           2, 2, */ case CLR_OpcodeParam_Var             : NANOCLR_WRITE_UNALIGNED_UINT16( ip, (CLR_UINT16)ref.m_index  ); break;
                /*#define ShortInlineI        1, 1, */ case CLR_OpcodeParam_ShortI          : NANOCLR_WRITE_UNALIGNED_INT8  ( ip, (CLR_INT8  )ref.m_arg_I4 ); break;
                /*#define ShortInlineR        4, 4, */ case CLR_OpcodeParam_ShortR          : NANOCLR_WRITE_UNALIGNED_INT32 ( ip,             ref.m_arg_R4 ); break;
                /*#define ShortInlineVar      1, 1, */ case CLR_OpcodeParam_ShortVar        : NANOCLR_WRITE_UNALIGNED_UINT8 ( ip, (CLR_UINT8 )ref.m_index  ); break;
            }
        }

        if(ip - ipStart != ref.m_ipLength)
        {
            wprintf( L"%s:\n", m_name.c_str() );
            wprintf( L"IL mismatch at %d: %d <> %d\n", i, ip - ipStart, ref.m_ipLength );

            DumpStats();
            NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
        }
    }

    //--//

    for(i=0; i<m_exceptions.size(); i++)
    {
        LogicalExceptionBlock& leb = m_exceptions[i];

        leb.m_TryOffset     = m_opcodes[ leb.m_TryIndex        ].m_ipOffset;
        leb.m_TryLength     = m_opcodes[ leb.m_TryIndexEnd     ].m_ipOffset + m_opcodes[ leb.m_TryIndexEnd     ].m_ipLength - leb.m_TryOffset;
        leb.m_HandlerOffset = m_opcodes[ leb.m_HandlerIndex    ].m_ipOffset;
        leb.m_HandlerLength = m_opcodes[ leb.m_HandlerIndexEnd ].m_ipOffset + m_opcodes[ leb.m_HandlerIndexEnd ].m_ipLength - leb.m_HandlerOffset;

        if (leb.m_Flags == COR_ILEXCEPTION_CLAUSE_FILTER)
        {
            leb.m_FilterOffset  = m_opcodes[ leb.m_FilterIndex ].m_ipOffset;
        }
    }

    s_numOfOpcodes[(int)m_opcodes   .size()]++;
    s_numOfEHs    [(int)m_exceptions.size()]++;
    s_sizeOfMethod[(int)code        .size()]++;

    NANOCLR_NOCLEANUP();
}
