//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace nanoFramework.Tools
{
    public static class CommandLineBuilderExtensions
    {
        public static void AppendSwitchIfTrue(this CommandLineBuilder commandLinedBuilder, string switchValue, bool value)
        {
            if(value)
            {
                commandLinedBuilder.AppendSwitch(switchValue);
            }
        }

        public static void AppendSwitchAndFiles(this CommandLineBuilder commandLinedBuilder, string switchName, ITaskItem[] fileListAsTaskItems)
        {
            // paranoid check if arguments have any data 
            if (fileListAsTaskItems?.Length > 0)
            {
                commandLinedBuilder.AppendSwitch(switchName);

                // loop through each file in fileListAsTaskItems
                foreach (ITaskItem file in fileListAsTaskItems)
                {
                    commandLinedBuilder.AppendFileNameIfNotNull(file);
                };
            }
        }

        public static void AppendSwitchForEachFile(this CommandLineBuilder commandLinedBuilder, string switchName, ITaskItem[] fileList)
        {
            if (fileList?.Length > 0)
            {

                foreach (ITaskItem file in fileList)
                {
                    commandLinedBuilder.AppendSwitch(switchName);
                    commandLinedBuilder.AppendFileNameIfNotNull(file.ItemSpec);
                };
            }
        }

        public static void AppendSwitchForFile(this CommandLineBuilder commandLinedBuilder, string switchName, string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                commandLinedBuilder.AppendSwitch(switchName);
                commandLinedBuilder.AppendFileNameIfNotNull(fileName);
            }
        }

        public static void AppendSwitchToFileAndExtraSwitches(this CommandLineBuilder commandLinedBuilder, string switchName, string fileName, params string[] extraSwitches)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                commandLinedBuilder.AppendSwitch(switchName);
                commandLinedBuilder.AppendFileNameIfNotNull(fileName);

                foreach (string val in extraSwitches)
                {
                    commandLinedBuilder.AppendSwitch(val);
                }
            }
        }
    }
}
