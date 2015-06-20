#region Imports

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#endregion

namespace PerfTest {
    #region Imports

    using System.Diagnostics;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Reflection.Emit;

    #endregion

    internal class Program {
        private static void Main () {
            /*
                Direct : 0:00:00.0000303
                Dynamic (8.00x) : 0:00:00.0002429
                Reflection (55.00x) : 0:00:00.0015782
                Precompiled (5.00x) : 0:00:00.0001476
                LazyCompiled (6.00x) : 0:00:00.000188
                ILEmitted (2.00x) : 0:00:00.0000602
                LazyILEmitted (3.00x) : 0:00:00.0001079
             */
            var foo = new Foo();
            var args = new object[0];
            var method = typeof(Foo).GetMethod("DoSomething");
            dynamic dfoo = foo;
            var precompiled = Expression.Lambda<Action>(Expression.Call(Expression.Constant(foo), method)).Compile();
            var lazyCompiled = new Lazy<Action>(() => Expression.Lambda<Action>(Expression.Call(Expression.Constant(foo), method)).Compile(), false);

            var wrapped = Wrap(method);
            var lazyWrapped = new Lazy<Func<object, object[], object>>(() => Wrap(method), false);
            var actions = new[]
            {
                new TimedAction("Direct", () => { foo.DoSomething(); }),
                new TimedAction("Dynamic", () => { dfoo.DoSomething(); }),
                new TimedAction("Reflection", () => { method.Invoke(foo, args); }),
                new TimedAction("Precompiled", () => { precompiled(); }),
                new TimedAction("LazyCompiled", () => { lazyCompiled.Value(); }),
                new TimedAction("ILEmitted", () => { wrapped(foo, null); }),
                new TimedAction("LazyILEmitted", () => { lazyWrapped.Value(foo, null); })
            };
            TimeActions(1000000, actions);

            Console.ReadKey();
        }

        private static void TimeActions (int loops, TimedAction[] actions) {
            var baseTicks = 0L;
            var sw = Stopwatch.StartNew();
            for (int i = 0, n = actions.Length; i < n; i++) {
                var actionItem = actions[i];
                var action = actionItem.Action;
                action(); // ignore precompile from the result, that will give a better real result

                sw.Restart();
                for (var j = 0; j < loops; j++) {
                    action();
                }
                sw.Stop();
                if (i == 0) {
                    baseTicks = sw.ElapsedTicks;
                    Console.WriteLine("{0} : {1:g}", actionItem.Title, sw.Elapsed);
                } else {
                    Console.WriteLine("{0} (-{2}x) : {1:g}", actionItem.Title, sw.Elapsed, (sw.ElapsedTicks * 1.0m / baseTicks).ToString("F3"));
                }
            }
        }

        private static Func<object, object[], object> Wrap (MethodInfo method) {
            var declaringType = method.DeclaringType;
            var dm = new DynamicMethod(method.Name, typeof(object), new[] { typeof(object), typeof(object[]) }, declaringType, true);
            var il = dm.GetILGenerator();

            if (!method.IsStatic) {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, declaringType);
            }
            var parameters = method.GetParameters();
            for (int i = 0, n = parameters.Length; i < n; i++) {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Unbox_Any, parameters[i].ParameterType);
            }
            il.EmitCall(method.IsStatic || declaringType.IsValueType ? OpCodes.Call : OpCodes.Callvirt, method, null);
            if (method.ReturnType == typeof(void)) {
                il.Emit(OpCodes.Ldnull);
            } else if (method.ReturnType.IsValueType) {
                il.Emit(OpCodes.Box, method.ReturnType);
            }
            il.Emit(OpCodes.Ret);
            return (Func<object, object[], object>)dm.CreateDelegate(typeof(Func<object, object[], object>));
        }

        private class Foo {
            public void DoSomething () {
            }
        }

        public class TimedAction {
            public TimedAction (string title, Action action) {
                Title = title;
                Action = action;
            }

            public Action Action { get; private set; }
            public string Title { get; private set; }
        }
    }
}