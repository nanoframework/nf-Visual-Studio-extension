//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

#include <stdafx.h>

#include <nanoHAL_v2.h>

#include <iostream>

////////////////////////////////////////////////////////////////////////////////////////////////////

bool HAL_Windows_IsShutdownPending() { return 0; }

void HAL_Windows_FastSleep(__int64) {}

// --- stubs required by Core.lib ----------------------

bool Watchdog_GetSetEnabled(bool enabled, bool fSet)
{
	return true;
}

void Watchdog_ResetCounter() {}

//void CPU_Reset() {}

//unsigned int Events_MaskedRead(unsigned int) { return 0; }

#if defined(PLATFORM_WINDOWS_EMULATOR)
void CLR_RT_EmulatorHooks::Notify_ExecutionStateChanged(void) {}
#endif

//unsigned int Events_WaitForEvents(unsigned int powerLevel, unsigned int, unsigned int) { return 0; }

void Events_SetboolTimer(int *, unsigned int) {}

//bool DebuggerPort_Flush(int) { return false; }

//bool DebuggerPort_IsSslSupported(COM_HANDLE ComPortNum) { return false; }

//bool DebuggerPort_UpgradeToSsl(COM_HANDLE ComPortNum, unsigned int flags) { return false; }

//bool DebuggerPort_IsUsingSsl(COM_HANDLE ComPortNum)
//{
//	return false;
//}


// ----------------------------------------------------------

int __cdecl hal_vprintf(const char* format, va_list arg)
{
	return vprintf_s(format, arg);
}

int __cdecl hal_printf(const char* format, ...)
{
	va_list arg_ptr;
	int     chars;

	va_start(arg_ptr, format);

	chars = hal_vprintf(format, arg_ptr);

	va_end(arg_ptr);

	return chars;
}

int __cdecl hal_vsnprintf(char* buffer, size_t len, const char* format, va_list arg)
{
	return _vsnprintf_s(buffer, len, len - 1/* force space for trailing zero*/, format, arg);
}

int __cdecl hal_snprintf(char * buffer, unsigned int len, char const * format, ...)
{
	va_list arg_ptr;
	int     chars;

	va_start(arg_ptr, format);

	chars = hal_vsnprintf(buffer, len, format, arg_ptr);

	va_end(arg_ptr);

	return chars;
}

void __cdecl HAL_Windows_Debug_Print(char * txt)
{
	std::cerr << txt << std::endl;
}

//unsigned int LCD_ConvertColor(UINT32 color)
//{
//	return color;
//}


// ----------------------------------------------------------

//#if !defined(BUILD_RTM)
//void HARD_Breakpoint()
//{
//	if (::IsDebuggerPresent())
//	{
//		::DebugBreak();
//	}
//}
//#endif
