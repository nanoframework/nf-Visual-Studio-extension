//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

#include <stdafx.h>

//UINT64 HAL_Time_CurrentTicks()
//{
//	return 0; //This method is currently not implemented
//}

//INT64 HAL_Time_TicksToTime(UINT64 Ticks)
//{
//	return 0;
//}

//INT64 HAL_Time_CurrentTime()
//{
//	return HAL_Time_TicksToTime(HAL_Time_CurrentTicks());
//}

unsigned __int64 HAL_Windows_GetPerformanceTicks(void)
{
	return 0;
}
