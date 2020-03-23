// all the [SyncVar] code from NetworkBehaviourProcessor in one place
using System.Collections.Generic;
using Mono.CecilX;
using Mono.CecilX.Cil;

namespace Mirror.Weaver
{
    public static class SyncVarProcessor
    {
        // ulong = 64 bytes
        const int SyncVarLimit = 64;

        // returns false for error, not for no-hook-exists
        public static bool CheckForHookFunction(TypeDefinition td, FieldDefinition syncVar, out MethodDefinition foundMethod)
        {
            foundMethod = null;
            foreach (CustomAttribute ca in syncVar.CustomAttributes)
            {
                if (ca.AttributeType.FullName == Weaver.SyncVarType.FullName)
                {
                    foreach (CustomAttributeNamedArgument customField in ca.Fields)
                    {
                        if (customField.Name == "hook")
                        {
                            string hookFunctionName = customField.Argument.Value as string;

                            foreach (MethodDefinition m in td.Methods)
                            {
                                if (m.Name == hookFunctionName)
                                {
                                    if (m.Parameters.Count == 2)
                                    {
                                        if (m.Parameters[0].ParameterType != syncVar.FieldType ||
                                            m.Parameters[1].ParameterType != syncVar.FieldType)
                                        {
                                            Weaver.Error($"{m} should have signature:\npublic void {hookFunctionName}({syncVar.FieldType} oldValue, {syncVar.FieldType} newValue) {{ }}");
                                            return false;
                                        }
                                        foundMethod = m;
                                        return true;
                                    }
                                    Weaver.Error($"{m} should have signature:\npublic void {hookFunctionName}({syncVar.FieldType} oldValue, {syncVar.FieldType} newValue) {{ }}");
                                    return false;
                                }
                            }
                            Weaver.Error($"No hook implementation found for {syncVar}. Add this method to your class:\npublic void {hookFunctionName}({syncVar.FieldType} oldValue, {syncVar.FieldType} newValue) {{ }}");
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public static MethodDefinition ProcessSyncVarGet(FieldDefinition fd, string originalName, FieldDefinition netFieldId)
        {
            //Create the get method
            MethodDefinition get = new MethodDefinition(
                    "get_Network" + originalName, MethodAttributes.Public |
                    MethodAttributes.SpecialName |
                    MethodAttributes.HideBySig,
                    fd.FieldType);

            ILProcessor getWorker = get.Body.GetILProcessor();

            // [SyncVar] GameObject?
            if (fd.FieldType.FullName == Weaver.gameObjectType.FullName)
            {
                // return this.GetSyncVarGameObject(ref field, uint netId);
                // this.
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldfld, netFieldId));
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldflda, fd));
                getWorker.Append(getWorker.Create(OpCodes.Call, Weaver.getSyncVarGameObjectReference));
                getWorker.Append(getWorker.Create(OpCodes.Ret));
            }
            // [SyncVar] NetworkIdentity?
            else if (fd.FieldType.FullName == Weaver.NetworkIdentityType.FullName)
            {
                // return this.GetSyncVarNetworkIdentity(ref field, uint netId);
                // this.
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldfld, netFieldId));
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldflda, fd));
                getWorker.Append(getWorker.Create(OpCodes.Call, Weaver.getSyncVarNetworkIdentityReference));
                getWorker.Append(getWorker.Create(OpCodes.Ret));
            }
            // [SyncVar] int, string, etc.
            else
            {
                getWorker.Append(getWorker.Create(OpCodes.Ldarg_0));
                getWorker.Append(getWorker.Create(OpCodes.Ldfld, fd));
                getWorker.Append(getWorker.Create(OpCodes.Ret));
            }

            get.Body.Variables.Add(new VariableDefinition(fd.FieldType));
            get.Body.InitLocals = true;
            get.SemanticsAttributes = MethodSemanticsAttributes.Getter;

            return get;
        }

        public static MethodDefinition ProcessSyncVarSet(TypeDefinition td, FieldDefinition fd, string originalName, long dirtyBit, FieldDefinition netFieldId)
        {
            //Create the set method
            MethodDefinition set = new MethodDefinition("set_Network" + originalName, MethodAttributes.Public |
                    MethodAttributes.SpecialName |
                    MethodAttributes.HideBySig,
                    Weaver.voidType);

            ILProcessor setWorker = set.Body.GetILProcessor();

            // if (!SyncVarEqual(value, ref playerData))
            Instruction endOfMethod = setWorker.Create(OpCodes.Nop);

            // this
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
            // new value to set
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_1));
            // reference to field to set
            // make generic version of SetSyncVar with field type
            if (fd.FieldType.FullName == Weaver.gameObjectType.FullName)
            {
                // reference to netId Field to set
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldfld, netFieldId));

                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.syncVarGameObjectEqualReference));
            }
            else if (fd.FieldType.FullName == Weaver.NetworkIdentityType.FullName)
            {
                // reference to netId Field to set
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldfld, netFieldId));

                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.syncVarNetworkIdentityEqualReference));
            }
            else
            {
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldflda, fd));

                GenericInstanceMethod syncVarEqualGm = new GenericInstanceMethod(Weaver.syncVarEqualReference);
                syncVarEqualGm.GenericArguments.Add(fd.FieldType);
                setWorker.Append(setWorker.Create(OpCodes.Call, syncVarEqualGm));
            }

            setWorker.Append(setWorker.Create(OpCodes.Brtrue, endOfMethod));

            // T oldValue = value;
            // TODO for GO/NI we need to backup the netId don't we?
            VariableDefinition oldValue = new VariableDefinition(fd.FieldType);
            set.Body.Variables.Add(oldValue);
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
            setWorker.Append(setWorker.Create(OpCodes.Ldfld, fd));
            setWorker.Append(setWorker.Create(OpCodes.Stloc, oldValue));

            // this
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));

            // new value to set
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_1));

            // reference to field to set
            setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
            setWorker.Append(setWorker.Create(OpCodes.Ldflda, fd));

            // dirty bit
            // 8 byte integer aka long
            setWorker.Append(setWorker.Create(OpCodes.Ldc_I8, dirtyBit));

            if (fd.FieldType.FullName == Weaver.gameObjectType.FullName)
            {
                // reference to netId Field to set
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldflda, netFieldId));

                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.setSyncVarGameObjectReference));
            }
            else if (fd.FieldType.FullName == Weaver.NetworkIdentityType.FullName)
            {
                // reference to netId Field to set
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldflda, netFieldId));

                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.setSyncVarNetworkIdentityReference));
            }
            else
            {
                // make generic version of SetSyncVar with field type
                GenericInstanceMethod gm = new GenericInstanceMethod(Weaver.setSyncVarReference);
                gm.GenericArguments.Add(fd.FieldType);

                // invoke SetSyncVar
                setWorker.Append(setWorker.Create(OpCodes.Call, gm));
            }

            CheckForHookFunction(td, fd, out MethodDefinition hookFunctionMethod);

            if (hookFunctionMethod != null)
            {
                //if (base.isLocalClient && !getSyncVarHookGuard(dirtyBit))
                Instruction label = setWorker.Create(OpCodes.Nop);
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.NetworkBehaviourIsLocalClient));
                setWorker.Append(setWorker.Create(OpCodes.Brfalse, label));
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldc_I8, dirtyBit));
                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.getSyncVarHookGuard));
                setWorker.Append(setWorker.Create(OpCodes.Brtrue, label));

                // setSyncVarHookGuard(dirtyBit, true);
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldc_I8, dirtyBit));
                setWorker.Append(setWorker.Create(OpCodes.Ldc_I4_1));
                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.setSyncVarHookGuard));

                // call hook (oldValue, newValue)
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldloc, oldValue));
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_1));
                setWorker.Append(setWorker.Create(OpCodes.Callvirt, hookFunctionMethod));

                // setSyncVarHookGuard(dirtyBit, false);
                setWorker.Append(setWorker.Create(OpCodes.Ldarg_0));
                setWorker.Append(setWorker.Create(OpCodes.Ldc_I8, dirtyBit));
                setWorker.Append(setWorker.Create(OpCodes.Ldc_I4_0));
                setWorker.Append(setWorker.Create(OpCodes.Call, Weaver.setSyncVarHookGuard));

                setWorker.Append(label);
            }

            setWorker.Append(endOfMethod);

            setWorker.Append(setWorker.Create(OpCodes.Ret));

            set.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.In, fd.FieldType));
            set.SemanticsAttributes = MethodSemanticsAttributes.Setter;

            return set;
        }

        public static void ProcessSyncVar(TypeDefinition td, FieldDefinition fd, Dictionary<FieldDefinition, FieldDefinition> syncVarNetIds, long dirtyBit)
        {
            string originalName = fd.Name;
            Weaver.DLog(td, "Sync Var " + fd.Name + " " + fd.FieldType + " " + Weaver.gameObjectType);

            // GameObject/NetworkIdentity SyncVars have a new field for netId
            FieldDefinition netIdField = null;
            if (fd.FieldType.FullName == Weaver.gameObjectType.FullName ||
                fd.FieldType.FullName == Weaver.NetworkIdentityType.FullName)
            {
                netIdField = new FieldDefinition("___" + fd.Name + "NetId",
                    FieldAttributes.Private,
                    Weaver.uint32Type);

                syncVarNetIds[fd] = netIdField;
            }

            MethodDefinition get = ProcessSyncVarGet(fd, originalName, netIdField);
            MethodDefinition set = ProcessSyncVarSet(td, fd, originalName, dirtyBit, netIdField);

            //NOTE: is property even needed? Could just use a setter function?
            //create the property
            PropertyDefinition propertyDefinition = new PropertyDefinition("Network" + originalName, PropertyAttributes.None, fd.FieldType)
            {
                GetMethod = get,
                SetMethod = set
            };

            //add the methods and property to the type.
            td.Methods.Add(get);
            td.Methods.Add(set);
            td.Properties.Add(propertyDefinition);
            Weaver.WeaveLists.replacementSetterProperties[fd] = set;

            // replace getter field if GameObject/NetworkIdentity so it uses
            // netId instead
            // -> only for GameObjects, otherwise an int syncvar's getter would
            //    end up in recursion.
            if (fd.FieldType.FullName == Weaver.gameObjectType.FullName ||
                fd.FieldType.FullName == Weaver.NetworkIdentityType.FullName)
            {
                Weaver.WeaveLists.replacementGetterProperties[fd] = get;
            }
        }

        public static void ProcessSyncVars(TypeDefinition td, List<FieldDefinition> syncVars, List<FieldDefinition> syncObjects, Dictionary<FieldDefinition, FieldDefinition> syncVarNetIds)
        {
            int numSyncVars = 0;

            // the mapping of dirtybits to sync-vars is implicit in the order of the fields here. this order is recorded in m_replacementProperties.
            // start assigning syncvars at the place the base class stopped, if any
            int dirtyBitCounter = Weaver.GetSyncVarStart(td.BaseType.FullName);

            syncVarNetIds.Clear();

            // find syncvars
            foreach (FieldDefinition fd in td.Fields)
            {
                foreach (CustomAttribute ca in fd.CustomAttributes)
                {
                    if (ca.AttributeType.FullName == Weaver.SyncVarType.FullName)
                    {
                        TypeDefinition resolvedField = fd.FieldType.Resolve();

                        if ((fd.Attributes & FieldAttributes.Static) != 0)
                        {
                            Weaver.Error($"{fd} cannot be static");
                            return;
                        }

                        if (fd.FieldType.IsArray)
                        {
                            Weaver.Error($"{fd} has invalid type. Use SyncLists instead of arrays");
                            return;
                        }

                        if (SyncObjectInitializer.ImplementsSyncObject(fd.FieldType))
                        {
                            Log.Warning($"{fd} has [SyncVar] attribute. SyncLists should not be marked with SyncVar");
                            break;
                        }

                        syncVars.Add(fd);

                        ProcessSyncVar(td, fd, syncVarNetIds, 1L << dirtyBitCounter);
                        dirtyBitCounter += 1;
                        numSyncVars += 1;

                        if (dirtyBitCounter == SyncVarLimit)
                        {
                            Weaver.Error($"{td} has too many SyncVars. Consider refactoring your class into multiple components");
                            return;
                        }
                        break;
                    }
                }

                if (fd.FieldType.Resolve().ImplementsInterface(Weaver.SyncObjectType))
                {
                    if (fd.IsStatic)
                    {
                        Weaver.Error($"{fd} cannot be static");
                        return;
                    }

                    syncObjects.Add(fd);
                }
            }

            // add all the new SyncVar __netId fields
            foreach (FieldDefinition fd in syncVarNetIds.Values)
            {
                td.Fields.Add(fd);
            }

            Weaver.SetNumSyncVars(td.FullName, numSyncVars);
        }
    }
}
