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

        // NanoCLR TypeDef token of the open generic (e.g. Box`1), local assembly only.
        // See CorDebug/CLAUDE.md "Pdbx.cs's TypeSpec/TypeSpecArg model".
        public Token GenericTypeDef { get; set; }

        // Cross-assembly fallback for GenericTypeDef: Cecil's unmodified FullName.
        public string GenericTypeDefName { get; set; }

        // Type arguments that close GenericTypeDef, in declaration order.
        public List<TypeSpecArg> GenericArguments { get; set; }
    }

    // One type argument of a closed generic instance (TypeSpec.GenericArguments).
    // See CorDebug/CLAUDE.md "Pdbx.cs's TypeSpec/TypeSpecArg model".
    public partial class TypeSpecArg
    {
        public bool IsPrimitive { get; set; }

        // Set when IsPrimitive: a NanoCLRDataType member name (e.g. "DATATYPE_I4").
        public string PrimitiveType { get; set; }

        // Local-assembly NanoCLR token (TypeDef or nested TypeSpec). Null for a foreign class --
        // see ClassName -- or for a generic-parameter argument -- see IsGenericParameter.
        public Token TypeToken { get; set; }

        // Cross-assembly fallback for TypeToken: Cecil's unmodified FullName.
        public string ClassName { get; set; }

        // True for a bare VAR/MVAR argument (e.g. T in Pair<T,int> inside the generic type that
        // declares T) rather than a closed type. TypeToken/ClassName are not used in this case.
        // See CorDebug/CLAUDE.md "Pdbx.cs's TypeSpec/TypeSpecArg model".
        public bool IsGenericParameter { get; set; }

        // Set when IsGenericParameter: the NanoCLR TBL_GenericParam token of the parameter's own
        // declaration.
        public Token GenericParamToken { get; set; }

        // Set when IsGenericParameter: true for a method type parameter (MVAR), false for a type
        // parameter (VAR).
        public bool GenericParamIsMethodOwned { get; set; }

        // Set when IsGenericParameter: zero-based position on its owner.
        public int GenericParamPosition { get; set; }
    }

    public partial class Member
    {
        public Token Token { get; set; }

        public string Name { get; set; }
    }

    public partial class Token
    {
        public string CLR { get; set; }
        public string NanoCLR { get; set; }
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
