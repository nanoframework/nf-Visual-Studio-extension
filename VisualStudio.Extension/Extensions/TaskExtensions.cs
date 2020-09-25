//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading.Tasks;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Extension to tell the compiler that we actually use the instance for something. This is that the "fire-and-forget" mechanism is being used intentionally.
        /// </summary>
        /// <param name="task"></param>
        public static void FireAndForget(this Task task)
        {
        }

        /// <summary>
        /// Extension to tell the compiler that we actually use the instance for something. This is that the "fire-and-forget" mechanism is being used intentionally.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task"></param>
        public static void FireAndForget<T>(this Task<T> task)
        {
        }
    }
}
