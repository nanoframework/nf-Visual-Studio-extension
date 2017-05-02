//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

#include "stdafx.h"
#include <nanoCLR_Types.h>

#pragma comment(lib, "Comdlg32")
#include <Commdlg.h>
////////////////////////////////////////////////////////////////////////////////////////////////////

struct Settings : CLR_RT_ParseOptions
{
	PELoader                       m_pe;
	MetaData::Collection           m_col;
	MetaData::Parser*              m_pr;
	bool                           m_fEE;
	CLR_RT_Assembly*               m_assm;
	CLR_RT_ParseOptions::BufferMap m_assemblies;

	bool                           m_fDumpStatistics;

	WatchAssemblyBuilder::Linker   m_lkForStrings;

	bool                           m_patch_fReboot;
	std::wstring                   m_patch_szNative;

	bool                           m_fFromAssembly;
	bool                           m_fFromImage;
	bool                           m_fNoByteCode;

	CLR_RT_StringSet                m_resources;

	//--//

	struct Command_Call : CLR_RT_ParseOptions::Command
	{
		typedef HRESULT(Settings::*FPN)(CLR_RT_ParseOptions::ParameterList* params);

		Settings& m_parent;
		FPN       m_call;

		Command_Call(Settings& parent, FPN call, LPCWSTR szName, LPCWSTR szDescription) : CLR_RT_ParseOptions::Command(szName, szDescription), m_parent(parent), m_call(call)
		{
		}

		virtual HRESULT Execute()
		{
			return (m_parent.*m_call)(&m_params);
		}
	};


	Settings()
	{
		m_fEE = false;

		m_fDumpStatistics = false;

		m_patch_fReboot = false;

		m_fFromAssembly = false;
		m_fFromImage = false;
		m_fNoByteCode = false;
		RevertToDefaults();

		BuildOptions();
	}

	~Settings()
	{
		Cmd_Reset();
	}

	//--//

	void RevertToDefaults()
	{
		for (CLR_RT_ParseOptions::BufferMapIter it = m_assemblies.begin(); it != m_assemblies.end(); it++)
		{
			delete it->second;
		}

		m_pe.Close();                                   // PELoader                       m_pe;
		m_col.Clear(false);                            // MetaData::Collection           m_col;
		m_pr = NULL;                       // MetaData::Parser*              m_pr;
										   // bool                           m_fEE;
		m_assm = NULL;                       // CLR_RT_Assembly*               m_assm;
		m_assemblies.clear();                            // CLR_RT_ParseOptions::BufferMap m_assemblies;
														 //
		m_fDumpStatistics = false;                      // bool                           m_fDumpStatistics;
														//
														// WatchAssemblyBuilder::Linker   m_lkForStrings;
														//
														// bool                           m_patch_fReboot;
														// bool                           m_patch_fSign;
														// std::wstring                   m_patch_szNative;
														//
		m_fFromAssembly = false;                      // bool                           m_fFromAssembly;
		m_fFromImage = false;                      // bool                           m_fFromImage;
												   // bool                           m_fNoByteCode;
	}

	//--//

	HRESULT AllocateSystem()
	{
		NANOCLR_HEADER();

		if (m_fEE == false)
		{
			NANOCLR_CHECK_HRESULT(CLR_RT_ExecutionEngine::CreateInstance());

			m_fEE = true;
		}

		NANOCLR_NOCLEANUP();
	}

	void ReleaseSystem()
	{
		if (m_fEE)
		{
			CLR_RT_ExecutionEngine::DeleteInstance();

			m_fEE = false;
		}
	}

	HRESULT CheckAssemblyFormat(CLR_RECORD_ASSEMBLY* header, LPCWSTR src)
	{
		NANOCLR_HEADER();

		if (header->GoodAssembly() == false)
		{
			wprintf(L"Invalid assembly format for '%s': ", src);
			for (int i = 0; i<sizeof(header->marker); i++)
			{
				wprintf(L"%02x", header->marker[i]);
			}
			wprintf(L"\n");

			NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
		}

		NANOCLR_NOCLEANUP();
	}

	//--//

#define PARAM_GENERIC(parm1Name,parm1Desc)     param = new CLR_RT_ParseOptions::Parameter_Generic(      parm1Name, parm1Desc ); cmd->m_params.push_back( param )
#define PARAM_STRING(val,parm1Name,parm1Desc)  param = new CLR_RT_ParseOptions::Parameter_String ( val, parm1Name, parm1Desc ); cmd->m_params.push_back( param )
#define PARAM_BOOLEAN(val,parm1Name,parm1Desc) param = new CLR_RT_ParseOptions::Parameter_Boolean( val, parm1Name, parm1Desc ); cmd->m_params.push_back( param )
#define PARAM_INTEGER(val,parm1Name,parm1Desc) param = new CLR_RT_ParseOptions::Parameter_Integer( val, parm1Name, parm1Desc ); cmd->m_params.push_back( param )
#define PARAM_FLOAT(val,parm1Name,parm1Desc)   param = new CLR_RT_ParseOptions::Parameter_Float  ( val, parm1Name, parm1Desc ); cmd->m_params.push_back( param )

#define PARAM_EXTRACT_STRING(lst,idx)    ((CLR_RT_ParseOptions::Parameter_Generic*)(*lst)[idx])->m_data.c_str()
#define PARAM_EXTRACT_BOOLEAN(lst,idx) *(((CLR_RT_ParseOptions::Parameter_Boolean*)(*lst)[idx])->m_dataPtr)


#define OPTION_GENERIC(optName,optDesc) cmd = new CLR_RT_ParseOptions::Command        (      optName, optDesc ); m_commands.push_back( cmd )
#define OPTION_SET(val,optName,optDesc) cmd = new CLR_RT_ParseOptions::Command_SetFlag( val, optName, optDesc ); m_commands.push_back( cmd )
#define OPTION_CALL(fpn,optName,optDesc) cmd = new Command_Call( *this, &Settings::fpn, optName, optDesc ); m_commands.push_back( cmd )

#define OPTION_STRING(val,optName,optDesc,parm1Name,parm1Desc)  OPTION_GENERIC(optName,optDesc); PARAM_STRING(val,parm1Name,parm1Desc)
#define OPTION_BOOLEAN(val,optName,optDesc,parm1Name,parm1Desc) OPTION_GENERIC(optName,optDesc); PARAM_BOOLEAN(val,parm1Name,parm1Desc)
#define OPTION_INTEGER(val,optName,optDesc,parm1Name,parm1Desc) OPTION_GENERIC(optName,optDesc); PARAM_INTEGER(val,parm1Name,parm1Desc)
#define OPTION_FLOAT(val,optName,optDesc,parm1Name,parm1Desc)   OPTION_GENERIC(optName,optDesc); PARAM_FLOAT(val,parm1Name,parm1Desc)

	HRESULT Cmd_Cfg(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		NANOCLR_CHECK_HRESULT(ExtractOptionsFromFile(PARAM_EXTRACT_STRING(params, 0)));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_Reset(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		RevertToDefaults();

		NANOCLR_NOCLEANUP_NOLABEL();
	}

	HRESULT Cmd_ResetHints(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_col.Clear(true);

		NANOCLR_NOCLEANUP_NOLABEL();
	}

	HRESULT Cmd_LoadHints(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		NANOCLR_CHECK_HRESULT(m_col.LoadHints(PARAM_EXTRACT_STRING(params, 0), PARAM_EXTRACT_STRING(params, 1)));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_IgnoreAssembly(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		NANOCLR_CHECK_HRESULT(m_col.IgnoreAssembly(PARAM_EXTRACT_STRING(params, 0)));

		NANOCLR_NOCLEANUP();
	}

	//--//

	HRESULT Cmd_Parse(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_fFromAssembly = true;
		m_fFromImage = false;

		if (!m_pr) NANOCLR_CHECK_HRESULT(m_col.CreateAssembly(m_pr));

		NANOCLR_CHECK_HRESULT(m_pr->Analyze(PARAM_EXTRACT_STRING(params, 0)));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_VerboseMinimize(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_CHECK_HRESULT(m_col.CreateAssembly(m_pr));

		m_pr->m_fVerboseMinimize = true;

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_NoByteCode(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_CHECK_HRESULT(m_col.CreateAssembly(m_pr));

		m_pr->m_fNoByteCode = true;
		m_fNoByteCode = true;

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_NoAttributes(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_CHECK_HRESULT(m_col.CreateAssembly(m_pr));

		m_pr->m_fNoAttributes = true;

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_ExcludeClassByName(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_CHECK_HRESULT(m_col.CreateAssembly(m_pr));

		m_pr->m_setFilter_ExcludeClassByName.insert(PARAM_EXTRACT_STRING(params, 0));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_Minimize(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);

		NANOCLR_CHECK_HRESULT(m_pr->RemoveUnused());

		NANOCLR_CHECK_HRESULT(m_pr->VerifyConsistency());

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_SaveStrings(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);

		{
			MetaData::Parser prCopy = *m_pr;

			NANOCLR_CHECK_HRESULT(m_lkForStrings.Process(prCopy));

			NANOCLR_CHECK_HRESULT(m_lkForStrings.SaveUniqueStrings(PARAM_EXTRACT_STRING(params, 0)));
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_LoadStrings(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		NANOCLR_CHECK_HRESULT(m_lkForStrings.LoadUniqueStrings(PARAM_EXTRACT_STRING(params, 0)));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_GenerateStringsTable(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		NANOCLR_CHECK_HRESULT(m_lkForStrings.DumpUniqueStrings(PARAM_EXTRACT_STRING(params, 0)));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_ImportResource(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_resources.insert(PARAM_EXTRACT_STRING(params, 0));

		NANOCLR_NOCLEANUP_NOLABEL();
	}

	HRESULT Cmd_Compile(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		if (!m_pr) NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);

		if (!m_pr) NANOCLR_SET_AND_LEAVE(E_FAIL);

		m_pr->m_resources = m_resources; m_resources.clear();

		{
			WatchAssemblyBuilder::Linker             lk;
			WatchAssemblyBuilder::CQuickRecord<BYTE> buf;
			MetaData::Parser                         prCopy = *m_pr;

			std::wstring                             szFile = PARAM_EXTRACT_STRING(params, 0);


			lk.LoadGlobalStrings();

			NANOCLR_CHECK_HRESULT(lk.Process(prCopy));

			NANOCLR_CHECK_HRESULT(lk.Generate(buf, m_patch_fReboot, m_patch_szNative.size() ? &m_patch_szNative : NULL));

			if (m_fDumpStatistics)
			{
				MetaData::ByteCode::DumpDistributionStats();
			}


			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::SaveFile(szFile.c_str(), (CLR_UINT8*)buf.Ptr(), (DWORD)buf.Size()));

			NANOCLR_CHECK_HRESULT(lk.DumpPdbx(szFile.c_str()));
		}

		NANOCLR_NOCLEANUP();
	}
	void AppendString(std::string& str, LPCSTR format, ...)
	{
		char    rgBuffer[512];
		LPSTR   szBuffer = rgBuffer;
		size_t  iBuffer = MAXSTRLEN(rgBuffer);
		va_list arg;

		va_start(arg, format);

		CLR_SafeSprintfV(szBuffer, iBuffer, format, arg);

		str.append(rgBuffer);
	}

	HRESULT Cmd_Load(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_fFromAssembly = false;
		m_fFromImage = true;

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		{
			LPCWSTR              szName = PARAM_EXTRACT_STRING(params, 0);
			CLR_RT_Buffer*       buffer = new CLR_RT_Buffer(); m_assemblies[szName] = buffer;
			CLR_RECORD_ASSEMBLY* header;
			CLR_RT_Assembly*     assm;

			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::LoadFile(szName, *buffer));

			header = (CLR_RECORD_ASSEMBLY*)&(*buffer)[0]; NANOCLR_CHECK_HRESULT(CheckAssemblyFormat(header, szName));

			NANOCLR_CHECK_HRESULT(CLR_RT_Assembly::CreateInstance(header, assm));

			g_CLR_RT_TypeSystem.Link(assm);
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_LoadDatabase(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_fFromAssembly = false;
		m_fFromImage = true;

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		{
			LPCWSTR              szFile = PARAM_EXTRACT_STRING(params, 0);
			CLR_RT_Buffer        buffer;
			CLR_RECORD_ASSEMBLY* header;
			CLR_RECORD_ASSEMBLY* headerEnd;
			std::wstring         strName;

			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::LoadFile(szFile, buffer));

			header = (CLR_RECORD_ASSEMBLY*)&buffer[0];
			headerEnd = (CLR_RECORD_ASSEMBLY*)&buffer[buffer.size() - 1];

			while (header + 1 <= headerEnd && header->GoodAssembly())
			{
				CLR_RT_Buffer*       bufferSub = new CLR_RT_Buffer();
				CLR_RECORD_ASSEMBLY* headerSub;
				CLR_RT_Assembly*     assm;

				bufferSub->resize(header->TotalSize());

				headerSub = (CLR_RECORD_ASSEMBLY*)&(*bufferSub)[0];

				if ((CLR_UINT8*)header + header->TotalSize() > (CLR_UINT8*)headerEnd)
				{
					//checksum passed, but not enough data in assembly
					_ASSERTE(FALSE);
					break;
				}
				memcpy(headerSub, header, header->TotalSize());

				NANOCLR_CHECK_HRESULT(CLR_RT_Assembly::CreateInstance(headerSub, assm));

				g_CLR_RT_TypeSystem.Link(assm);

				CLR_RT_UnicodeHelper::ConvertFromUTF8(assm->m_szName, strName); m_assemblies[strName] = bufferSub;

				header = (CLR_RECORD_ASSEMBLY*)ROUNDTOMULTIPLE((size_t)header + header->TotalSize(), CLR_UINT32);
			}
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_DumpAll(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR szName = PARAM_EXTRACT_STRING(params, 0);

		if (szName[0] == 0) szName = NULL;

		if (m_fFromAssembly && m_pr)
		{
			m_pr->DumpSchema(szName, m_fNoByteCode);
		}
		else
		{
			NANOCLR_CHECK_HRESULT(AllocateSystem());

			g_CLR_RT_TypeSystem.Dump(szName, m_fNoByteCode);
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_DumpDat(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		m_fFromAssembly = false;
		m_fFromImage = true;

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		{
			LPCWSTR              szFile = PARAM_EXTRACT_STRING(params, 0);
			CLR_RT_Buffer        buffer;
			CLR_RECORD_ASSEMBLY* header;
			CLR_RECORD_ASSEMBLY* headerEnd;
			std::wstring         strName;

			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::LoadFile(szFile, buffer));

			header = (CLR_RECORD_ASSEMBLY*)&buffer[0];
			headerEnd = (CLR_RECORD_ASSEMBLY*)&buffer[buffer.size() - 1];

			int number = 0;

			while (header + 1 <= headerEnd && header->GoodAssembly())
			{
				CLR_RT_Buffer*       bufferSub = new CLR_RT_Buffer();
				CLR_RECORD_ASSEMBLY* headerSub;
				CLR_RT_Assembly*     assm;

				bufferSub->resize(header->TotalSize());

				headerSub = (CLR_RECORD_ASSEMBLY*)&(*bufferSub)[0];

				if ((CLR_UINT8*)header + header->TotalSize() > (CLR_UINT8*)headerEnd)
				{
					//checksum passed, but not enough data in assembly
					_ASSERTE(FALSE);
					break;
				}
				memcpy(headerSub, header, header->TotalSize());

				NANOCLR_CHECK_HRESULT(CLR_RT_Assembly::CreateInstance(headerSub, assm));

				//CLR_RT_UnicodeHelper::ConvertFromUTF8( assm->m_szName, strName ); m_assemblies[strName] = bufferSub;

				printf("Assembly %d: %s (%d.%d.%d.%d), size: %d\r\n", ++number, assm->m_szName, header->version.iMajorVersion, header->version.iMinorVersion, header->version.iBuildNumber, header->version.iRevisionNumber, header->TotalSize());

				// jump to next assembly
				header = (CLR_RECORD_ASSEMBLY*)ROUNDTOMULTIPLE((size_t)header + header->TotalSize(), CLR_UINT32);
			}
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_DumpExports(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR szName = PARAM_EXTRACT_STRING(params, 0);

		if (szName[0] == 0) szName = NULL;

		if (m_fFromAssembly && m_pr)
		{
			m_pr->DumpCompact(szName);
		}
		else
		{
			NANOCLR_SET_AND_LEAVE(CLR_E_FAIL);
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_GenerateSkeleton(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR     szFile = PARAM_EXTRACT_STRING(params, 0);
		LPCWSTR     szName = PARAM_EXTRACT_STRING(params, 1);
		LPCWSTR     szProj = PARAM_EXTRACT_STRING(params, 2);
		LPCWSTR     szLeg = PARAM_EXTRACT_STRING(params, 3);


		BOOL fUseOldCodeGen = _wcsicmp(L"TRUE", szLeg) == 0;

		std::string name;

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		if (szFile[0] == 0) szFile = NULL;

		CLR_RT_UnicodeHelper::ConvertToUTF8(szName, name);

		m_assm = g_CLR_RT_TypeSystem.FindAssembly(name.c_str(), NULL, false);
		if (m_assm)
		{
			if (fUseOldCodeGen)
			{
				m_assm->GenerateSkeleton_Legacy(szFile, szProj);
			}
			else
			{
				m_assm->GenerateSkeleton(szFile, szProj);
			}
		}

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_RefreshAssembly(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR     szName = PARAM_EXTRACT_STRING(params, 0);
		LPCWSTR     szFile = PARAM_EXTRACT_STRING(params, 1);
		std::string name;

		CLR_RT_UnicodeHelper::ConvertToUTF8(szName, name);

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		m_assm = g_CLR_RT_TypeSystem.FindAssembly(name.c_str(), NULL, false);
		if (m_assm)
		{
			CLR_UINT32 len = m_assm->m_header->TotalSize();

			if (len % sizeof(CLR_UINT32))
			{
				len += sizeof(CLR_UINT32) - (len % sizeof(CLR_UINT32));
			}

			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::SaveFile(szFile, (CLR_UINT8*)m_assm->m_header, (DWORD)len));
		}

		NANOCLR_NOCLEANUP();
	}

#if defined(NANOCLR_JITTER)
	HRESULT Cmd_Jit(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR     szName = PARAM_EXTRACT_STRING(params, 0);
		std::string name;

		CLR_RT_UnicodeHelper::ConvertToUTF8(szName, name);

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		m_assm = g_CLR_RT_TypeSystem.FindAssembly(name.c_str(), NULL, false);
		if (m_assm)
		{
			for (int i = 0; i<m_assm->m_pTablesSize[TBL_MethodDef]; i++)
			{
				CLR_RT_MethodDef_Index md;

				md.Set(m_assm->m_idx, i);

				NANOCLR_CHECK_HRESULT(g_CLR_RT_ExecutionEngine.Compile(md, CLR_RT_ExecutionEngine::c_Compile_CPP));
			}
		}

		NANOCLR_NOCLEANUP();
	}
#endif

	HRESULT Cmd_Resolve(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		bool fError = false;

		NANOCLR_CHECK_HRESULT(AllocateSystem());

		NANOCLR_FOREACH_ASSEMBLY(g_CLR_RT_TypeSystem)
		{
			const CLR_RECORD_ASSEMBLYREF* src = (const CLR_RECORD_ASSEMBLYREF*)pASSM->GetTable(TBL_AssemblyRef);
			for (int i = 0; i<pASSM->m_pTablesSize[TBL_AssemblyRef]; i++, src++)
			{
				LPCSTR szName = pASSM->GetString(src->name);

				if (g_CLR_RT_TypeSystem.FindAssembly(szName, &src->version, true) == NULL)
				{
					printf("Missing assembly: %s (%d.%d.%d.%d)\r\n", szName, src->version.iMajorVersion, src->version.iMinorVersion, src->version.iBuildNumber, src->version.iRevisionNumber);

					fError = true;
				}
			}
		}
		NANOCLR_FOREACH_ASSEMBLY_END();

		if (fError) NANOCLR_SET_AND_LEAVE(CLR_E_ENTRY_NOT_FOUND);

		NANOCLR_CHECK_HRESULT(g_CLR_RT_TypeSystem.ResolveAll());

		NANOCLR_NOCLEANUP();
	}

	//--//

	HRESULT Cmd_GenerateDependency__OutputAssembly(CLR_XmlUtil xml, IXMLDOMNode* node, IXMLDOMNodePtr& assmNode, LPCWSTR szTag, CLR_RT_Assembly* assm)
	{
		NANOCLR_HEADER();

		std::wstring name;
		WCHAR        rgBuffer[1024];
		bool         fFound;

		CLR_RT_UnicodeHelper::ConvertFromUTF8(assm->m_szName, name);
		swprintf(rgBuffer, ARRAYSIZE(rgBuffer), L"%d.%d.%d.%d", assm->m_header->version.iMajorVersion, assm->m_header->version.iMinorVersion, assm->m_header->version.iBuildNumber, assm->m_header->version.iRevisionNumber);

		NANOCLR_CHECK_HRESULT(xml.CreateNode(szTag, &assmNode, node));

		NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Name", name, fFound, assmNode));
		NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Version", rgBuffer, fFound, assmNode));
		NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Hash", WatchAssemblyBuilder::ToHex(assm->ComputeAssemblyHash()), fFound, assmNode));
		NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Flags", WatchAssemblyBuilder::ToHex(assm->m_header->flags), fFound, assmNode));

		NANOCLR_NOCLEANUP();
	}

	HRESULT Cmd_GenerateDependency(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		LPCWSTR     szFile = PARAM_EXTRACT_STRING(params, 0);
		CLR_XmlUtil xml;

		NANOCLR_CHECK_HRESULT(xml.New(L"AssemblyGraph"));

		NANOCLR_FOREACH_ASSEMBLY(g_CLR_RT_TypeSystem)
		{
			IXMLDOMNodePtr assmNode;

			NANOCLR_CHECK_HRESULT(Cmd_GenerateDependency__OutputAssembly(xml, NULL, assmNode, L"Assembly", pASSM));

			{
				const CLR_RECORD_ASSEMBLYREF* src = (const CLR_RECORD_ASSEMBLYREF*)pASSM->GetTable(TBL_AssemblyRef);
				for (int i = 0; i<pASSM->m_pTablesSize[TBL_AssemblyRef]; i++, src++)
				{
					IXMLDOMNodePtr   assmRefNode;
					CLR_RT_Assembly* assmRef = g_CLR_RT_TypeSystem.FindAssembly(pASSM->GetString(src->name), &src->version, true); if (!assmRef) NANOCLR_SET_AND_LEAVE(CLR_E_NULL_REFERENCE);

					NANOCLR_CHECK_HRESULT(Cmd_GenerateDependency__OutputAssembly(xml, assmNode, assmRefNode, L"AssemblyRef", assmRef));
				}
			}

			{
				const CLR_RECORD_TYPEDEF*      src = pASSM->GetTypeDef(0);
				CLR_RT_TypeDef_CrossReference* dst = pASSM->m_pCrossReference_TypeDef;

				for (int i = 0; i<pASSM->m_pTablesSize[TBL_TypeDef]; i++, src++, dst++)
				{
					IXMLDOMNodePtr       typeNode;
					CLR_RT_TypeDef_Index td; td.Set(pASSM->m_idx, i);
					char                 rgBuffer[512];
					LPSTR                szBuffer = rgBuffer;
					size_t               iBuffer = MAXSTRLEN(rgBuffer);
					std::wstring         name;
					bool                 fFound;

					g_CLR_RT_TypeSystem.BuildTypeName(td, szBuffer, iBuffer);

					//
					// Skip types used by the runtime.
					//
					if (strchr(rgBuffer, '<')) continue;
					if (strchr(rgBuffer, '>')) continue;
					if (strchr(rgBuffer, '$')) continue;

					CLR_RT_UnicodeHelper::ConvertFromUTF8(rgBuffer, name);

					 NANOCLR_CHECK_HRESULT(xml.CreateNode(L"Type", &typeNode, assmNode));

					 NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Name", name, fFound, typeNode));
					 NANOCLR_CHECK_HRESULT(xml.PutAttribute(NULL, L"Hash", WatchAssemblyBuilder::ToHex(dst->m_hash), fFound, typeNode));
				}
			}
		}
		NANOCLR_FOREACH_ASSEMBLY_END();

		NANOCLR_CHECK_HRESULT(xml.Save(szFile));

		NANOCLR_NOCLEANUP();
	}

	//--//

	HRESULT Cmd_CreateDatabase(CLR_RT_ParseOptions::ParameterList* params = NULL)
	{
		NANOCLR_HEADER();

		CLR_RT_StringVector vec;
		CLR_RT_StringVectorIter iter;
		CLR_RT_StringVectorIter current;
		CLR_RT_Buffer       database;
		size_t              pos;

		NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::ExtractTokensFromFile(PARAM_EXTRACT_STRING(params, 0), vec));

		// Delete duplicate assemblies
		current = vec.begin();
		while (current != vec.end())
		{
			iter = current;
			iter++;
			while (iter != vec.end())
			{
				if (0 == (*current).compare(*iter))
				{
					iter = vec.erase(iter);
				}
				else
				{
					iter++;
				}
			}
			if (current != vec.end()) current++;
		}



		for (size_t j = 0; j<vec.size(); j++)
		{
			CLR_RT_Buffer buffer;

			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::LoadFile(vec[j].c_str(), buffer));

			pos = ROUNDTOMULTIPLE(database.size(), CLR_UINT32);

			database.resize(pos + buffer.size());

			memcpy(&database[pos], &buffer[0], buffer.size());
		}

		//
		// Add a group of zeros at the end, the device will stop at that point.
		//
		pos = ROUNDTOMULTIPLE(database.size(), CLR_UINT32);
		database.resize(pos + sizeof(CLR_UINT32));
		{
			NANOCLR_CHECK_HRESULT(CLR_RT_FileStore::SaveFile(PARAM_EXTRACT_STRING(params, 1), database));
		}
		NANOCLR_NOCLEANUP();
	}

	void Usage()
	{
		wprintf(L"\nnanoFramework MetaDataProcessor Utility v1.0.0\r\n");

		wprintf(L"Available command line switches:\n\n");

		CLR_RT_ParseOptions::Usage();
	}

	void BuildOptions()
	{
		CLR_RT_ParseOptions::Command*   cmd;
		CLR_RT_ParseOptions::Parameter* param;

		OPTION_SET(&m_fVerbose, L"-verbose", L"Outputs each command before executing it");

		OPTION_INTEGER(&s_CLR_RT_fTrace_RedirectLinesPerFile, L"-Trace_RedirectLinesPerFile", L"", L"<lines>", L"Lines per File");
		OPTION_STRING(&s_CLR_RT_fTrace_RedirectOutput, L"-Trace_RedirectOutput", L"", L"<file>", L"Output file");

		OPTION_SET(&m_fDumpStatistics, L"-ILstats", L"Dumps statistics about IL code");

		//--//

		OPTION_CALL(Cmd_Reset, L"-reset", L"Clears all previous configuration");

		//--//

		OPTION_CALL(Cmd_ResetHints, L"-resetHints", L"Clears all previous DLL hints");

		OPTION_CALL(Cmd_LoadHints, L"-loadHints", L"Loads a specific file as a dependency");
		PARAM_GENERIC(L"<assembly>", L"Name of the assembly to process");
		PARAM_GENERIC(L"<file>", L"File for the assembly");

		//--//

		OPTION_CALL(Cmd_IgnoreAssembly, L"-ignoreAssembly", L"Doesn't include an assembly in the dependencies");
		PARAM_GENERIC(L"<assembly>", L"Assembly to ignore");

		//--//

		OPTION_CALL(Cmd_Parse, L"-parse", L"Analyzes .NET assembly");
		PARAM_GENERIC(L"<file>", L"File to analyze");

		//--//

		OPTION_SET(&m_patch_fReboot, L"-patchReboot", L"Marks the patch as needing a reboot");
		OPTION_STRING(&m_patch_szNative, L"-patchNative", L"ARM code to include in the patch", L"<file>", L"Native code file");

		//--//

		OPTION_CALL(Cmd_Cfg, L"-cfg", L"Loads configuration from a file");
		PARAM_GENERIC(L"<file>", L"Config file to load");

		OPTION_CALL(Cmd_VerboseMinimize, L"-verboseMinimize", L"Turns on verbose level for the minimization phase");

		OPTION_CALL(Cmd_NoByteCode, L"-noByteCode", L"Skips any ByteCode present in the assembly");

		OPTION_CALL(Cmd_NoAttributes, L"-noAttributes", L"Skips any attribute present in the assembly");

		OPTION_CALL(Cmd_ExcludeClassByName, L"-excludeClassByName", L"Removes a class from an assembly");
		PARAM_GENERIC(L"<class>", L"Class to exclude");

		OPTION_CALL(Cmd_Minimize, L"-minimize", L"Minimizes the assembly, removing unwanted elements");

		OPTION_CALL(Cmd_SaveStrings, L"-saveStrings", L"Saves strings table to a file");
		PARAM_GENERIC(L"<file>", L"Output file");

		OPTION_CALL(Cmd_LoadStrings, L"-loadStrings", L"Loads strings table from file");
		PARAM_GENERIC(L"<file>", L"Input file");

		OPTION_CALL(Cmd_GenerateStringsTable, L"-generateStringsTable", L"Outputs the collected database of strings");
		PARAM_GENERIC(L"<file>", L"Output file");

		OPTION_CALL(Cmd_ImportResource, L"-importResource", L"Imports .nanoresources file");
		PARAM_GENERIC(L"<file>", L"File to load");

		OPTION_CALL(Cmd_Compile, L"-compile", L"Compiles an assembly into the nanoCLR format");
		PARAM_GENERIC(L"<file>", L"Generated filename");

		OPTION_CALL(Cmd_Load, L"-load", L"Loads an assembly formatted for nanoCLR");
		PARAM_GENERIC(L"<file>", L"File to load");

		OPTION_CALL(Cmd_LoadDatabase, L"-loadDatabase", L"Loads a set of assemblies");
		PARAM_GENERIC(L"<file>", L"Image to load");

		OPTION_CALL(Cmd_DumpAll, L"-dump_all", L"Generates a report of an assembly's metadata");
		PARAM_GENERIC(L"<file>", L"Report file");

		OPTION_CALL(Cmd_DumpDat, L"-dump_dat", L"dumps the pe files in a dat file together with their size");
		PARAM_GENERIC(L"<file>", L"Dat file");

		OPTION_CALL(Cmd_DumpExports, L"-dump_exports", L"Generates a report of an assembly's metadata, more readable format");
		PARAM_GENERIC(L"<file>", L"Report file");

		OPTION_CALL(Cmd_GenerateSkeleton, L"-generate_skeleton", L"Generates a skeleton for the methods implemented in native code");
		PARAM_GENERIC(L"<file>", L"Prefix name for the files");
		PARAM_GENERIC(L"<name>", L"Name of the assembly");
		PARAM_GENERIC(L"<project>", L"Identifier for the library");
		PARAM_GENERIC(L"<true|false>", L"Use legacy interop method signature");

		OPTION_CALL(Cmd_RefreshAssembly, L"-refresh_assembly", L"Recomputes CRCs for an assembly");
		PARAM_GENERIC(L"<name>", L"Name of the assembly");
		PARAM_GENERIC(L"<output>", L"Output file");

		OPTION_CALL(Cmd_Resolve, L"-resolve", L"Tries to resolve cross-assembly references");

#if defined(NANOCLR_JITTER)
		OPTION_CALL(Cmd_Jit, L"-jit", L"Generate JIT code");
		PARAM_GENERIC(L"<name>", L"Name of the assembly");

		OPTION_INTEGER(&s_CLR_RT_fJitter_Trace_Statistics, L"-Jitter_Trace_Statistics", L"", L"<level>", L"Level of verbosity");
		OPTION_INTEGER(&s_CLR_RT_fJitter_Trace_Compile, L"-Jitter_Trace_Compile", L"", L"<level>", L"Level of verbosity");
#endif

		OPTION_CALL(Cmd_GenerateDependency, L"-generate_dependency", L"Generate an XML file with the relationship between assemblies");
		PARAM_GENERIC(L"<file>", L"Output file");

		//--//

		OPTION_CALL(Cmd_CreateDatabase, L"-create_database", L"Creates file database for a device");
		PARAM_GENERIC(L"<config>", L"File containing the Bill of Materials");
		PARAM_GENERIC(L"<file>", L"Output file");
	}
};

//--//

extern int s_CLR_RT_fTrace_AssemblyOverhead;

//--//

int _tmain(int argc, _TCHAR* argv[])
{
	NANOCLR_HEADER();

	CLR_RT_Assembly::InitString();

	CLR_RT_StringVector vec;
	Settings            st;

	::CoInitialize(0);

	NANOCLR_CHECK_HRESULT(HAL_Windows::Memory_Resize(64 * 1024 * 1024));
	// TODO check if we are still using this.....
	//HAL_Init_Custom_Heap();

	// do not display assembly load information
	s_CLR_RT_fTrace_AssemblyOverhead = 0;

	CLR_RT_Memory::Reset();

	st.PushArguments(argc - 1, argv + 1, vec);

	NANOCLR_CHECK_HRESULT(st.ProcessOptions(vec));

	NANOCLR_CLEANUP();

	if (FAILED(hr))
	{
		ErrorReporting::Print(NULL, NULL, TRUE, 0, L"%S", CLR_RT_DUMP::GETERRORMESSAGE(hr));
		fflush(stdout);
	}

	::CoUninitialize();

	return FAILED(hr) ? 10 : 0;
}
