//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//
#include "stdafx.h"

unsigned int HAL_Time_CurrentSysTicks()
{
	// TODO need to check if using the Win32 100ns ticks works
	return 0; // UNDONE: FIXME: EmulatorNative::GetITimeDriver()->CurrentTicks();
}
