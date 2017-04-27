//
// Copyright (c) 2017 The nanoFramework project contributors
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
                commandLinedBuilder.AppendSwitch("-minimize");
            }
        }

        public static void AppendSwitchAndFiles(this CommandLineBuilder commandLinedBuilder, string switchName, ITaskItem[] fileList)
        {
            // paranoid check if property has any data 
            if (fileList?.Length > 0)
            {
                commandLinedBuilder.AppendSwitch(switchName);

                foreach(ITaskItem file in fileList)
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

        //public static void AppendSwitchToFileAndExtraSwitches(this CommandLineBuilder commandLinedBuilder, string switchValue, string file, )
        //{
        //    // paranoid check if property has any data 
        //    if (fileList?[0]?.MetadataCount > 0)
        //    {
        //        commandLinedBuilder.AppendSwitch(switchValue);

        //        fileList.Select(stringFile => {

        //            commandLinedBuilder.AppendFileNameIfNotNull(stringFile);

        //            return new object();
        //        }).ToList();
        //    }
        //}
    }
}
