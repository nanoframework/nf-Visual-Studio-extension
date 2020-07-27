//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

#include <stdafx.h>


#define COM_DEBUG                           ConvertCOM_DebugHandle(0)

#define DEBUG_TEXT_PORT    COM_DEBUG
#define DEBUGGER_PORT      COM_DEBUG
#define MESSAGING_PORT     COM_DEBUG

////////////////////////////////////////////////////////////////////////////////////////////////////
#if defined(PLATFORM_WINDOWS_EMULATOR)
HAL_Configuration_Windows g_HAL_Configuration_Windows;
#endif

HAL_SYSTEM_CONFIG HalSystemConfig =
{
	{ true }, // HAL_DRIVER_CONFIG_HEADER Header;

			  //--//

			  // COM_HANDLE      DebuggerPorts[MAX_DEBUGGERS];
	{
		DEBUGGER_PORT,
	},

	DEBUG_TEXT_PORT,
	115200,
	0,  // STDIO = COM2 or COM1

	{ 0, 0 },   // { SRAM1_MEMORY_Base, SRAM1_MEMORY_Size },
	{ 0, 0 },   // { FLASH_MEMORY_Base, FLASH_MEMORY_Size },
};

static unsigned char* s_Memory_Start = NULL;
static unsigned int s_Memory_Length = 1024 * 1024 * 10;

void HeapLocation(unsigned char*& BaseAddress, unsigned int& SizeInBytes)
{
	if (!s_Memory_Start)
	{
		s_Memory_Start = (unsigned char*)::VirtualAlloc(NULL, s_Memory_Length, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);

		if (s_Memory_Start)
		{
			memset(s_Memory_Start, 0xEA, s_Memory_Length);
		}

		HalSystemConfig.RAM1.Base = (UINT32)(size_t)s_Memory_Start;
		HalSystemConfig.RAM1.Size = (UINT32)(size_t)s_Memory_Length;
	}

	BaseAddress = s_Memory_Start;
	SizeInBytes = s_Memory_Length;
}

static UINT8* s_CustomHeap_Start = NULL;

void CustomHeapLocation(unsigned char*& BaseAddress, unsigned int& SizeInBytes)
{
	if (!s_CustomHeap_Start)
	{
		s_CustomHeap_Start = (unsigned char*)::VirtualAlloc(NULL, s_Memory_Length, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);

		if (s_CustomHeap_Start)
		{
			memset(s_CustomHeap_Start, 0xEA, s_Memory_Length);
		}
	}

	BaseAddress = s_CustomHeap_Start;
	SizeInBytes = s_Memory_Length;
}

//--//

HRESULT HAL_Windows::Memory_Resize(unsigned int size)
{
	NANOCLR_HEADER();

	if (s_Memory_Start)
	{
		::VirtualFree(s_Memory_Start, 0, MEM_RELEASE);

		s_Memory_Start = NULL;
	}

	if (s_CustomHeap_Start)
	{
		::VirtualFree(s_CustomHeap_Start, 0, MEM_RELEASE);

		s_CustomHeap_Start = NULL;
	}

	s_Memory_Length = size;

	NANOCLR_NOCLEANUP_NOLABEL();
}
