using System.Collections;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    private static object? Call(Type type, string name, object? target, params object?[] args) =>
        type.GetMethod(name, All)!.Invoke(target, args);
    private static object? Get(object target, string name) => target.GetType().GetProperty(name, All)!.GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, All)!.SetValue(target, value);
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        Console.WriteLine("PASS|" + message);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("参数：宿主程序集目录 插件DLL路径");
            var hostDir = Path.GetFullPath(args[0]);
            var pluginPath = Path.GetFullPath(args[1]);
            // 在独立进程中加载真实宿主程序集，不启动宿主窗口或真实编码任务。
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                var path = Path.Combine(hostDir, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(hostDir, "FFmpegFreeUI.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(hostDir, "LakeUI.dll"));
            var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
            Console.WriteLine("HOST|" + host.GetName().Version);

            // 按元数据解析每个直接宿主成员引用，覆盖尚未进入运行分支的二进制签名。
            using (var pe = new PEReader(File.OpenRead(pluginPath)))
            {
                var reader = pe.GetMetadataReader();
                int checkedMembers = 0;
                foreach (var handle in reader.MemberReferences)
                {
                    var member = reader.GetMemberReference(handle);
                    if (member.Parent.Kind != HandleKind.TypeReference) continue;
                    var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    var scope = type.ResolutionScope;
                    while (scope.Kind == HandleKind.TypeReference)
                        scope = reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
                    if (scope.Kind != HandleKind.AssemblyReference) continue;
                    var assemblyName = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                    if (assemblyName is not ("FFmpegFreeUI" or "LakeUI")) continue;
                    plugin.ManifestModule.ResolveMember(MetadataTokens.GetToken(handle));
                    checkedMembers++;
                }
                Check(checkedMembers > 0, "宿主及 LakeUI 成员签名 " + checkedMembers);
            }

            var queueType = host.GetType("FFmpegFreeUI.编码队列_v6", true)!;
            var taskType = host.GetType("FFmpegFreeUI.编码任务_v6", true)!;
            var task = Activator.CreateInstance(taskType)!;
            var id = (string)Get(task, "ID")!;
            Set(task, "任务名称", "兼容测试_日本語_任务");
            // 只向测试进程中的内存队列插入，不调用会自动调度或写缓存的添加接口。
            var queueField = queueType.GetFields(All).Single(f =>
                typeof(IList).IsAssignableFrom(f.FieldType) && f.FieldType.IsGenericType &&
                f.FieldType.GetGenericArguments()[0] == taskType);
            var queue = (IList)queueField.GetValue(null)!;
            queue.Add(task);
            var index = queueType.GetField("任务索引", All)?.GetValue(null) as IDictionary;
            index?.Add(id, task);
            taskType.GetField("正在执行标记", All)!.SetValue(task, true);
            var statusType = host.GetType("FFmpegFreeUI.编码任务状态_v6", true)!;
            Set(task, "状态", Enum.Parse(statusType, "正在处理"));

            foreach (var name in new[] { "BackendProgress", "PauseControl", "StopControl", "PreviewEngine" })
            {
                var type = plugin.GetType("videoenhancer." + name, true)!;
                var method = name == "PreviewEngine" ? "FindTaskById" : "FindTask";
                Check(ReferenceEquals(Call(type, method, null, id), task), name + " 按 ID 查找");
                Check(Call(type, method, null, "missing") is null, name + " 缺失任务");
            }
            var previewType = plugin.GetType("videoenhancer.PreviewEngine", true)!;
            using var preview = (IDisposable)Activator.CreateInstance(previewType, All, null, new object?[] { null, null }, null)!;
            var active = (IList)Call(previewType, "CollectActiveTasks", preview)!;
            Check(active.Count == 1 && (string)Get(active[0]!, "Id")! == id, "预览枚举执行中任务");

            var manager = host.GetType("FFmpegFreeUI.插件管理", true)!;
            var progress = plugin.GetType("videoenhancer.BackendProgress", true)!;
            Action<string, object> subscribe = (name, callback) => Call(manager, "注册编码队列事件", null, name, callback);
            Call(progress, "Attach", null, subscribe);
            void Emit(string name, string? text = null)
            {
                object? log = null;
                if (text is not null)
                {
                    log = Activator.CreateInstance(host.GetType("FFmpegFreeUI.编码任务日志条目_v6", true)!)!;
                    Set(log, "文本", text);
                }
                Call(manager, "编码队列事件处理", null, name, task, log);
            }
            Emit("task.log", "BACKEND: Total Output Frames: 100");
            Emit("task.progress", "FPS: 2.50 Current Frame: 25 ETA: 00:00:30");
            var telemetry = Call(progress, "GetTelemetry", null, id)!;
            Check(telemetry is not null && (long)telemetry.GetType().GetField("Frame")!.GetValue(telemetry)! == 25,
                "真实宿主 JSON 事件转为预览遥测");
            Check(Math.Abs((double)Get(Get(task, "进度")!, "百分比")! - .25) < .0001, "队列进度回写 25%");

            var shm = "ve_compat_" + Guid.NewGuid().ToString("N");
            using var memory = MemoryMappedFile.CreateNew(shm, 1);
            using var view = memory.CreateViewAccessor();
            Set(task, "命令行", "videoenhancer.exe -pause-shm " + shm + " -stop-shm " + shm);
            Emit("task.paused");
            Check(view.ReadByte(0) == 1, "暂停事件写入共享内存");
            Emit("task.resumed");
            Check(view.ReadByte(0) == 0, "恢复事件写入共享内存");
            Call(plugin.GetType("videoenhancer.StopControl", true)!, "StopTask", null, task);
            Check(view.ReadByte(0) == 1 && (bool)Get(task, "手动停止")!, "停止信号及手动停止状态");
            Emit("task.completed");
            Check(Call(progress, "GetTelemetry", null, id) is null, "任务终态清理遥测");
            taskType.GetField("正在执行标记", All)!.SetValue(task, false);
            Check(((IList)Call(previewType, "CollectActiveTasks", preview)!).Count == 0, "已结束任务退出活动预览列表");
            queue.Remove(task);
            index?.Remove(id);
            Console.WriteLine("HOST_COMPATIBILITY_PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
