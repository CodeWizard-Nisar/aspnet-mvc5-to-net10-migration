using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: PatchFrameworkServices <bin folder> <path to ConfReg.MonoShims.dll>
// Redirects calls to the two HostingEnvironment members Mono lacks to ConfReg.MonoShims.HostingShim. Idempotent.
var bin = args[0];
var shimPath = args[1];
File.Copy(shimPath, Path.Combine(bin, Path.GetFileName(shimPath)), overwrite: true);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(bin);
using var shimAsm = AssemblyDefinition.ReadAssembly(shimPath, new ReaderParameters { AssemblyResolver = resolver });
var shimType = shimAsm.MainModule.GetType("ConfReg.MonoShims.HostingShim");

int total = 0;
var il = (Mono.Cecil.Cil.ILProcessor?)null;
foreach (var file in Directory.GetFiles(bin, "Microsoft.AspNetCore.SystemWebAdapters*.dll"))
{
    var asm = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadWrite = true, AssemblyResolver = resolver });
    int n = 0;
    foreach (var type in asm.MainModule.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var ins in method.Body.Instructions.ToList())
            {
                // System.Web.HttpRequest.ReadEntityBodyMode (and its enum) are .NET 4.5 members Mono's System.Web lacks.
                // "Classic" (1) is what the request is in when nothing has opted in to buffering: the safe answer.
                if (ins.Operand is MethodReference rb && rb.DeclaringType.FullName == "System.Web.HttpRequest" && rb.Name == "get_ReadEntityBodyMode")
                {
                    var proc = method.Body.GetILProcessor();
                    ins.OpCode = OpCodes.Pop; ins.Operand = null;           // drop the HttpRequest receiver
                    proc.InsertAfter(ins, proc.Create(OpCodes.Ldc_I4_1));   // push ReadEntityBodyMode.Classic
                    n++;
                    continue;
                }
                if (ins.Operand is MethodReference cd && cd.DeclaringType.FullName == "System.Web.HttpResponse" && cd.Name == "get_ClientDisconnectedToken")
                {
                    var proc = method.Body.GetILProcessor();
                    var never = shimType.Methods.Single(m => m.Name == "NeverCancelled");
                    ins.OpCode = OpCodes.Pop; ins.Operand = null;                                  // drop the HttpResponse receiver
                    proc.InsertAfter(ins, proc.Create(OpCodes.Call, asm.MainModule.ImportReference(never)));
                    n++;
                    continue;
                }
                if (ins.Operand is not MethodReference mr || mr.DeclaringType.FullName != "System.Web.Hosting.HostingEnvironment") continue;
                string? target = mr.Name switch
                {
                    "get_IsDevelopmentEnvironment" => "IsDevelopmentEnvironment",
                    "QueueBackgroundWorkItem" => "QueueBackgroundWorkItem",
                    _ => null
                };
                if (target is null) continue;
                var replacement = shimType.Methods.Single(m => m.Name == target && m.Parameters.Count == mr.Parameters.Count);
                ins.Operand = asm.MainModule.ImportReference(replacement);
                n++;
            }
    if (n > 0) { asm.Write(); Console.WriteLine($"patched {n} call(s) in {Path.GetFileName(file)}"); total += n; }
    else asm.Dispose();
}
Console.WriteLine(total == 0 ? "nothing to patch (already patched?)" : $"done: {total} call(s) redirected");
