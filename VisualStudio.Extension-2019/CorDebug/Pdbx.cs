//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Any changes in these classes need to be replicated on the same class at the Metadata processor //
    /////////////////////////////////////////////////////////////////////////////////////////////////////

    public partial class Pdbx
    {
        public Assembly Assembly { get; set; }
    }

    public partial class Assembly
    {
        public Token Token { get; set; }

        public string FileName { get; set; }

        [JsonConverter(typeof(VersionConverter))]
        public Version Version { get; set; }

        public List<Class> Classes { get; set; }

        public List<GenericParam> GenericParams { get; set; }

        public List<TypeSpec> TypeSpecs { get; set; }
    }

    public partial class Class
    {
        public Token Token { get; set; }

        public string Name { get; set; }
        public bool IsEnum { get; set; } = false;
        public int NumGenericParams { get; set; } = 0;
        public bool IsGenericInstance { get; set; } = false;

        public List<Method> Methods { get; set; }
        public List<Field> Fields { get; set; }
    }

    public partial class Field
    {
        public string Name { get; set; }
        public Token Token { get; set; }
    }

    public partial class Method
    {
        public Token Token { get; set; }

        public string Name { get; set; }
        public int NumParams { get; set; } = 0;
        public int NumLocals { get; set; } = 0;
        public int NumGenericParams { get; set; } = 0;
        public bool IsGenericInstance { get; set; } = false;
        public bool HasByteCode { get; set; } = false;
        public List<IL> ILMap { get; set; }
    }

    public partial class IL
    {
        public Token Token { get; set; }
    }

    public partial class GenericParam
    {
        public Token Token { get; set; }
       
        public string Name { get; set; }
    }

    public partial class TypeSpec
    {
        public Token Token { get; set; }

        public string Name { get; set; }
        public bool IsGenericInstance { get; set; } = false;

        public List<Member> Members { get; set; }
    }

    public partial class Member
    {
        public Token Token { get; set; }

        public string Name { get; set; }
    }

    public partial class Token
    {
        public string Clr { get; set; }
        public string NanoClr { get; set; }
    }

    #region Converters

    public class VersionConverter : JsonConverter<Version>
    {
        public override Version Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
                Version.Parse(reader.GetString());

        public override void Write(
            Utf8JsonWriter writer,
            Version value,
            JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString(4));
    }

    #endregion

}
