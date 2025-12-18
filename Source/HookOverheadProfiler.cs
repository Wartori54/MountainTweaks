using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using MethodAttributes = System.Reflection.MethodAttributes;
using MethodImplAttributes = System.Reflection.MethodImplAttributes;
using TypeAttributes = System.Reflection.TypeAttributes;

namespace Celeste.Mod.MountainTweaks;

public class HookOverheadProfiler {
    public static readonly HookOverheadProfiler Instance = new();

    // ReSharper disable once InconsistentNaming
    private static readonly FieldInfo DetourManager_detourStates = typeof(DetourManager).GetField("detourStates", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly List<Hook> profilerHooks = [];
    private readonly IdToMethod idToMethod = new();
    private readonly Stopwatch timer = new();
    
    private bool running = false;
    private AssemblyBuilder? assemblyBuilder = null;
    
    private static List<MethodBase> GetDetourList() {
        IDictionary detourDict = (IDictionary) DetourManager_detourStates.GetValue(null)!;
        
        List<MethodBase> methodList = detourDict.Keys.Cast<MethodBase>().ToList();
        return methodList;
    }
    

    public void Start() {
        if (running) return;
        running = true;
        idToMethod.Reset();
        
        List<(MethodBase, Promise<Type>, Type[])> queuedPromises = new();
        foreach (MethodBase method in GetDetourList()) {
            MethodDetourInfo detourInfo = DetourManager.GetDetourInfo(method);
            if (!detourInfo.IsDetoured) continue;
            (Promise<Type> delegateType, Type[] parameters) = GenerateMethodDelegateAndParameterList(method);
            if (delegateType.Done)
                InstallOnMethod(method, delegateType.Result, parameters);
            else
                queuedPromises.Add((method, delegateType, parameters));
        }
        if (queuedPromises.Count != 0) {
            assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(nameof(HookOverheadProfiler) + "_DelegateAsm_" + DateTime.Now.Microsecond), 
                AssemblyBuilderAccess.RunAndCollect);
            ModuleBuilder mb = assemblyBuilder.DefineDynamicModule("MainModule");
            foreach ((MethodBase method, Promise<Type> promise, Type[] paramList) in queuedPromises) {
                promise.Yield(mb);
                if (!promise.Done) throw new NotSupportedException("Promise didn't finish!");
                Type delegateType = promise.Result;
                InstallOnMethod(method, delegateType, paramList);
            }
        }
        foreach (Hook hook in profilerHooks) {
            hook.Apply();
        }
        timer.Start();
    }

    private void InstallOnMethod(MethodBase method, Type delegateType, Type[] parameters) {
        DynamicMethodDefinition dmd = new($"HookOverheadProfilerHook_{method.DeclaringType?.FullName}.{method.Name}", 
            SafeGetReturnType(method), 
            parameters.Prepend(delegateType).ToArray());
        new ILContext(dmd.Definition).Invoke(il => {
            ILCursor cursor = new(il);
            int methodId = idToMethod.For(method);
            cursor.EmitLdcI4(methodId);
            cursor.EmitCall(((Delegate)HookCallback).Method);
            cursor.EmitLdarg0();
            for (int i = 1; i < il.Method.Parameters.Count; i++) {
                cursor.EmitLdarg(i);
            }
            cursor.EmitCallvirt(delegateType.GetMethod("Invoke")!);
            cursor.EmitRet();
        });
        MethodInfo srDmd = dmd.Generate();
        try {
            profilerHooks.Add(new Hook(method, srDmd, new DetourConfig("HookOverheadProfiler", int.MaxValue), false));
        } catch (Exception ex) {
            Logger.Error(nameof(HookOverheadProfiler), $"Failed to hook method {method.DeclaringType?.FullName ?? "(Unknown decl type)"}.{method.Name}({string.Join(",", method.GetParameters().Select(x => x.ToString()))})");
            Logger.Error(nameof(HookOverheadProfiler), $"Using hook method {srDmd.DeclaringType?.FullName ?? "(Unknown decl type)"}.{srDmd.Name}({string.Join(",", srDmd.GetParameters().Select(x => x.ToString()))})");
            Logger.LogDetailed(ex);
        }
    }

    public void Stop() {
        running = false;
        foreach (Hook hook in profilerHooks) {
            hook.Dispose();
        }
        timer.Stop();
        profilerHooks.Clear();
        assemblyBuilder = null;
        PrintStats();
        idToMethod.Reset();
        timer.Reset();
    }

    private static void HookCallback(int methodId) {
        Instance.idToMethod.RegisterCallFor(methodId);
    }

    private static Type SafeGetReturnType(MethodBase method) {
        return method switch {
            MethodInfo methodInfo => methodInfo.ReturnType,
            ConstructorInfo => typeof(void),
            _ => throw new NotSupportedException(),
        };
    }

    private static (Promise<Type>, Type[]) GenerateMethodDelegateAndParameterList(MethodBase method) {
        if (!method.CallingConvention.HasFlag(CallingConventions.Standard)) 
            throw new NotSupportedException("Method calling convention not supported");
        // TODO: all the calls to ToArray here are ugly, better way???
        ParameterInfo[] parameters = method.GetParameters();
        List<Type> parameterTypes = parameters.Select(p => p.ParameterType).ToList();
        if (!method.IsStatic) {
            if (method.DeclaringType == null) {
                throw new NotSupportedException("Can't hook method with no declaring type");
            }
            // Value types have its instance passed by ref
            Type selfType = method.DeclaringType.IsValueType ? method.DeclaringType.MakeByRefType() : method.DeclaringType;
            parameterTypes.Insert(0, selfType);
        }
        Type returnType = SafeGetReturnType(method);
        Promise<Type> delegateType = GenerateDelegateTypeFor(returnType, parameterTypes);
        return (delegateType, parameterTypes.ToArray());
    }

    private static Promise<Type> GenerateDelegateTypeFor(Type returnType, List<Type> parameterTypes) {
        Type delegateType;
        if (parameterTypes.Count <= 16 && IsValidGeneric(returnType) && parameterTypes.All(IsValidGeneric)) {
            if (returnType != typeof(void)) {
                delegateType = Type.GetType($"System.Func`{parameterTypes.Count + 1}")!.MakeGenericType(parameterTypes.Append(returnType).ToArray());
            } else {
                if (parameterTypes.Count == 0) { // Parameter-less actions are generic-less
                    delegateType = Type.GetType("System.Action")!;
                } else {
                    delegateType = Type.GetType($"System.Action`{parameterTypes.Count}")!.MakeGenericType(parameterTypes.ToArray());
                }
            }
            return Promise<Type>.For(delegateType);
        }
        return Promise<Type>.For(ctx => GenerateDelegateTypeForWithTb(returnType, parameterTypes, (ctx as ModuleBuilder)!));

        static bool IsValidGeneric(Type t) {
            return !t.IsByRef && !t.IsByRefLike && !t.IsPointer && !t.IsFunctionPointer && !t.IsUnmanagedFunctionPointer;
        }
    }

    private static ulong _delegateIdx = 0;
    private static Type GenerateDelegateTypeForWithTb(Type returnType, List<Type> parameterTypes, ModuleBuilder mb) {
        TypeBuilder tBuilder = mb.DefineType($"AuxiliaryTypeDelegate{_delegateIdx++}", 
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed,
            typeof(MulticastDelegate));

        tBuilder.DefineConstructor(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                                   | MethodAttributes.RTSpecialName,
            CallingConventions.Standard, [typeof(object), typeof(nint)])
            .SetImplementationFlags(MethodImplAttributes.Runtime);
        
        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/delegates#203-delegate-members
        // specifies that the only required extra members for a Delegate is the `Invoke` method, thus, `BeingInvoke` and others are not required
        tBuilder.DefineMethod("Invoke", MethodAttributes.Virtual | MethodAttributes.Public 
                                                                 | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            CallingConventions.Standard | CallingConventions.HasThis,
            returnType, parameterTypes.ToArray()
        ).SetImplementationFlags(MethodImplAttributes.Runtime);
        
        return tBuilder.CreateType();
    }

    private void PrintStats() {
        const double timeUnit = 1f/60;
        ulong totalCallCount = 0;
        PriorityQueue<(MethodBase m, ulong totalc, int mult), double> pq = new();
        Console.WriteLine($"Profiled for: {timer.Elapsed.TotalSeconds:F2} sec");
        for (int i = 0; i < idToMethod.TotalCalls.Count; i++) {
            MethodBase method = idToMethod.For(i);
            MethodDetourInfo detourInfo = DetourManager.GetDetourInfo(method);
            int totalHooks = detourInfo.Detours.Count() + detourInfo.ILHooks.Count();
            double score = idToMethod.TotalCalls[i] / timer.Elapsed.TotalSeconds * timeUnit * totalHooks;
            pq.Enqueue((method, idToMethod.TotalCalls[i], totalHooks), -score);
            totalCallCount += idToMethod.TotalCalls[i] * (ulong)totalHooks;
        }
        while (pq.TryDequeue(out (MethodBase m, ulong totalc, int mult) data, out double negScore)) {
            Console.WriteLine($"Score for {data.m.DeclaringType?.FullName}.{data.m.Name}: {-negScore:F2} (call count: {data.totalc}, mult: {data.mult})");
        }
        Console.WriteLine($"Overall score: {totalCallCount/timer.Elapsed.TotalSeconds*timeUnit:F2} ({totalCallCount})");
    }

    public static void PrintAllDetours() {
        foreach (MethodBase method in GetDetourList()) {
            Console.WriteLine($"{method.DeclaringType?.FullName}.{method.Name}");
            MethodDetourInfo detourInfo = DetourManager.GetDetourInfo(method);
            foreach (DetourInfo di in detourInfo.Detours) {
                Console.WriteLine($"--> [OnHook] {di.Entry.DeclaringType}.{di.Entry.Name}");
            }
            foreach (ILHookInfo hi in detourInfo.ILHooks) {
                Console.WriteLine($"--> [ILHook] {hi.ManipulatorMethod.DeclaringType}.{hi.ManipulatorMethod.Name}");
            }
        }
    }

    private class IdToMethod {
        private readonly Dictionary<int, MethodBase> idToMethod = new();
        private readonly Dictionary<MethodBase, int> methodToId = new();
        public List<ulong> TotalCalls { get; private set; } = [];
        private int currId = 0;

        public int For(MethodBase method) {
            if (methodToId.TryGetValue(method, out int id)) {
                return id;
            }

            methodToId[method] = currId;
            idToMethod[currId] = method;
            TotalCalls.Add(0);
            return currId++;
        }

        public MethodBase For(int id) {
            return idToMethod[id];
        }

        public void RegisterCallFor(int id) {
            TotalCalls[id]++;
        }

        public void Reset() {
            TotalCalls.Clear();
            currId = 0;
            idToMethod.Clear();
            methodToId.Clear();
        }
    }

    private class Promise<T> {
        public bool Done { get; private set; }
        public T Result => Done ? result! : throw new InvalidOperationException();

        private T? result;
        private readonly Func<object, T>? cb;

        private Promise(T resultV) {
            result = resultV;
            Done = true;
        }

        private Promise(Func<object, T> callback) {
            cb = callback;
            Done = false;
            result = default;
        }

        public void Yield(object context) {
            if (Done) return;
            if (cb == null) throw new InvalidOperationException();
            result = cb(context);
            Done = true;
        }

        public static Promise<T> For(T resultV) {
            return new Promise<T>(resultV);
        }

        public static Promise<T> For(Func<object, T> callback) {
            return new Promise<T>(callback);
        }
    }
}