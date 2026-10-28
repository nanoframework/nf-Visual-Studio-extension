# Generic instance display: Pdbx model + CorDebugTypeParameter

Companion to `Pdbx.cs` and `CorDebugType.cs` in this directory. Captures the
reasoning behind displaying closed generic types (`Box<int>` rather than
``Box`1``) in Locals/Watch/Autos.

**Documentation policy:** source comments in this directory stay minimal —
one line flagging a non-obvious invariant, with a `see CorDebug/CLAUDE.md
"Section Title"` pointer left in the code.

---

## 1. `Pdbx.cs`'s `TypeSpec`/`TypeSpecArg` model

This file is a manually-synced mirror of
`nanoframework/metadata-processor`'s own `Pdbx.cs` (pulled in here as the
`metadata-processor` git submodule) — see that file's own header comment.
The full derivation of the model — why arguments are addressed by NanoCLR
token rather than CLR token, why classes need a name fallback, why bare
generic parameters (VAR/MVAR) get their own representation, and the
empirical Mono.Cecil findings behind all three — lives in
**`metadata-processor/MetadataProcessor.Shared/Pdbx/CLAUDE.md`** in that
repo. Keep this file's comments in sync with that copy's when either
changes.

Short version: a generic instance's own arguments can't reliably be
addressed by Cecil's `MetadataToken` (RID 0 for anything not backed by a
real PE `TypeSpec` row — most commonly a type used only as a field or
return type), so every reference here is a NanoCLR token instead, resolved
the same way the wire protocol already resolves them. Ordinary classes and
the open TypeDef carry a Cecil-name fallback (`ClassName`/
`GenericTypeDefName`) for the case where they're declared in a different
assembly than the TypeSpec, since this model has no `TypeRef` table. A bare
generic parameter (e.g. the `T` in `Pair<T,int>` used inside the generic
type `Container<T>` that declares `T`) is recorded explicitly
(`IsGenericParameter` + its own `TBL_GenericParam` token), not resolved as
if it were an ordinary class.

## 2. Consumer side: `CorDebugTypeParameter`

The CLR reports a generic instance as `DATATYPE_CLASS`/`DATATYPE_VALUETYPE`
with `HB_GenericInstance` set, `m_td` the open TypeDef and `m_ts` the closed
TypeSpec. `CorDebugTypeParameter.FromRuntimeValue` resolves `m_ts` to its
`Pdbx.TypeSpec` and walks `GenericArguments`, resolving each against the
*owning* assembly (the one that owns the TypeSpec, not the caller's), since
`TypeToken`/`GenericParamToken` are local-assembly tokens (§1).

Fails closed on the whole instance if *any* argument can't be resolved,
rather than rendering a name with holes — the caller
(`EnumerateTypeParameters`) then returns `E_NOTIMPL`, which is exactly the
behaviour that shipped before this feature, so an unresolvable instance
still renders as the open type rather than breaking.

`ClassFromRuntimeValue` (`CorDebugValue.cs`) deliberately keeps resolving
the class from `m_td` (the open TypeDef), not `m_ts`. `ICorDebugClass.GetToken`
must return a TypeDef token for `IMetaDataImport` to name the type; a
`CorDebugClass` built from a TypeSpec only carries a TypeSpec token. VS's
stock expression evaluator is what composes the closed display name, from
`GetClass` (open TypeDef) plus `EnumerateTypeParameters` (the arguments
above) — the extension never hands VS a pre-built type-name string.

A *nested* generic-instance argument (`TypeSpecArg.TypeToken` pointing at
another TypeSpec, §1) resolves through `GetClassFromNanoCLRToken`, which for
a TypeSpec token constructs `CorDebugClass(assembly, typeSpec)` —
`_pdbxClass` null, `_pdbxTypeSpec` set. `GetToken()`/`GetModule()` on that
instance resolve `_pdbxTypeSpec.GenericTypeDef` (local assembly, the common
case) or, when that's null, `GenericTypeDefName` through `ClassFromFullName`
(`ForeignOpenTypeClass`) and delegate to *that* class — together, so a
caller always gets a token and a module from the same metadata scope, never
this assembly's module paired with a foreign token. `ForeignOpenTypeClass`
can't recurse: `GetClassFromFullName` only ever searches `Classes`
(TypeDefs), so the class it returns is always `_pdbxClass`-backed, one hop.

## 3. Why `IsGenericInst` was not reused for the new flag

`RuntimeValue.IsGenericInst` (nf-debugger) reported the old, removed
`DATATYPE_GENERICINST` heap-block datatype. That datatype can no longer
occur, so `IsGenericInst` is now always `false` and `RuntimeValue_Generic`
(its only `true` implementation) is unreachable — both are dead code left
in place in nf-debugger, not deleted; only the two branches that *consumed*
them here in `CorDebugValue.cs` were removed (the `IsGenericInst` check that
routed to `CorDebugValueBoxedObject`, and the `DATATYPE_GENERICINST` branch
in `ClassFromRuntimeValue`).

Redefining `IsGenericInst` to mean the new `RuntimeValue.IsGenericInstance`
flag instead of deleting it would have revived the first of those two
branches, routing every generic instance through `CorDebugValueBoxedObject`
— wrong, since a generic instance is not boxed. The new flag has its own
name for exactly this reason.

## 4. Cross-assembly `ClassFromFullName` ambiguity

`CorDebugAppDomain.ClassFromFullName` has exactly one caller:
`CorDebugTypeParameter.Resolve`'s fallback for `TypeSpecArg.ClassName` (§1),
reached only *after* a local-token lookup against the owning assembly has
already failed — i.e. the class is expected to live in a *different* loaded
assembly than the one that owns the TypeSpec. Filtering the search to the
owning assembly's identity would therefore make this fallback always fail;
`TypeSpecArg.ClassName` carries no assembly identity to filter by anyway (it's
Cecil's bare `FullName`). Instead, if more than one loaded assembly declares
a class with that exact name, the method returns `null` (ambiguous) rather
than silently returning whichever assembly happened to be enumerated first —
same fail-closed philosophy as §2.
