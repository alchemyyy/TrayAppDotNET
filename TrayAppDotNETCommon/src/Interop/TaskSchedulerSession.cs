using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// One connection to the local Task Scheduler, working on folders directly under its root. Members return the HRESULT
/// instead of throwing; <see cref="IsNotFound"/> tells a missing folder or task apart from a failure. The caller holds a
/// <see cref="ComApartmentScope"/> for the session's lifetime.
/// </summary>
internal sealed class TaskSchedulerSession : IDisposable
{
    private static readonly Guid TaskSchedulerClassID = new("0f87369f-a4e5-4cfc-bd3e-73e6154572dd");

    private const int ErrorFileNotFound = unchecked((int)0x80070002);
    private const int ErrorPathNotFound = unchecked((int)0x80070003);
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);
    private const int ErrorDirectoryNotEmpty = unchecked((int)0x80070091);

    // TASK_CREATE_OR_UPDATE, TASK_LOGON_INTERACTIVE_TOKEN and TASK_ENUM_HIDDEN from taskschd.h
    private const int CreateOrUpdate = 6;
    private const int InteractiveTokenLogon = 3;
    private const int EnumerateHidden = 1;

    private const string RootFolderPath = @"\";

    private ITaskService? _service;

    private TaskSchedulerSession(ITaskService service) => _service = service;

    /// <summary>True for the HRESULT of a folder or task that does not exist.</summary>
    public static bool IsNotFound(int hResult) => hResult is ErrorFileNotFound or ErrorPathNotFound;

    /// <summary>True for the HRESULT of a folder <see cref="DeleteFolder"/> kept because it still holds tasks or folders.</summary>
    public static bool IsNotEmpty(int hResult) => hResult == ErrorDirectoryNotEmpty;

    /// <summary>Connects to the local Task Scheduler as the current user.</summary>
    public static int TryConnect(out TaskSchedulerSession? session)
    {
        session = null;
        ITaskService? service = null;
        try
        {
            Guid serviceInterfaceID = typeof(ITaskService).GUID;
            int hr = Ole32.CoCreateInstance(
                in TaskSchedulerClassID,
                IntPtr.Zero,
                Ole32.CLSCTX_INPROC_SERVER,
                in serviceInterfaceID,
                out IntPtr instance);
            if (hr < 0) return hr;

            service = COMActivation.GetObjectForComInstance<ITaskService>(instance, releaseInputReference: true);
            // Empty arguments: the local computer, as the current user
            hr = service.Connect(default, default, default, default);
            if (hr < 0) return hr;

            session = new TaskSchedulerSession(service);
            service = null;
            return hr;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or InvalidCastException)
        {
            return exception.HResult;
        }
        finally
        {
            Safe.Release(service);
        }
    }

    /// <summary>Opens a task; a missing folder or task returns a not-found HRESULT.</summary>
    public int TryOpenTask(string folderName, string taskName, out ScheduledTask? task)
    {
        task = null;
        int hr = GetFolder(FolderPath(folderName), out ITaskFolder? folder);
        if (hr < 0 || folder == null) return hr;
        try
        {
            hr = folder.GetTask(taskName, out IRegisteredTask? registered);
            if (hr >= 0 && registered != null) task = new ScheduledTask(registered);
            return hr;
        }
        finally
        {
            Safe.Release(folder);
        }
    }

    /// <summary>
    /// Registers a task from its XML definition, replacing a task of that name, with an interactive-token principal
    /// for <paramref name="userID"/> and the given security descriptor. Creates the folder when it is missing.
    /// </summary>
    public int RegisterTask(string folderName, string taskName, string xml, string userID, string securityDescriptor)
    {
        int hr = GetOrCreateFolder(folderName, out ITaskFolder? folder);
        if (hr < 0 || folder == null) return hr;

        TaskVariant user = TaskVariant.FromString(userID);
        TaskVariant security = TaskVariant.FromString(securityDescriptor);
        try
        {
            hr = folder.RegisterTask(
                taskName,
                xml,
                CreateOrUpdate,
                user,
                password: default,
                InteractiveTokenLogon,
                security,
                out IRegisteredTask? registered);
            Safe.Release(registered);
            return hr;
        }
        finally
        {
            user.Free();
            security.Free();
            Safe.Release(folder);
        }
    }

    /// <summary>Deletes one task; a missing folder or task returns a not-found HRESULT.</summary>
    public int DeleteTask(string folderName, string taskName)
    {
        int hr = GetFolder(FolderPath(folderName), out ITaskFolder? folder);
        if (hr < 0 || folder == null) return hr;
        try
        {
            return folder.DeleteTask(taskName, flags: 0);
        }
        finally
        {
            Safe.Release(folder);
        }
    }

    /// <summary>
    /// Deletes every task in the folder, hidden ones included, whose name <paramref name="select"/> accepts. Returns the
    /// first failure but keeps deleting the rest; a missing folder returns a not-found HRESULT.
    /// </summary>
    public int DeleteTasks(string folderName, Func<string, bool> select)
    {
        ArgumentNullException.ThrowIfNull(select);
        int hr = GetFolder(FolderPath(folderName), out ITaskFolder? folder);
        if (hr < 0 || folder == null) return hr;
        try
        {
            hr = ReadTaskNames(folder, out List<string> names);
            if (hr < 0) return hr;

            int firstFailure = 0;
            foreach (string name in names)
            {
                if (!select(name)) continue;
                int deleteResult = folder.DeleteTask(name, flags: 0);
                if (deleteResult < 0 && !IsNotFound(deleteResult) && firstFailure == 0) firstFailure = deleteResult;
            }

            return firstFailure;
        }
        finally
        {
            Safe.Release(folder);
        }
    }

    /// <summary>
    /// Deletes a folder under the root. Task Scheduler keeps a folder that still holds tasks or folders and reports
    /// <see cref="IsNotEmpty"/>; a missing folder returns a not-found HRESULT.
    /// </summary>
    public int DeleteFolder(string folderName)
    {
        int hr = GetFolder(RootFolderPath, out ITaskFolder? root);
        if (hr < 0 || root == null) return hr;
        try
        {
            return root.DeleteFolder(folderName, flags: 0);
        }
        finally
        {
            Safe.Release(root);
        }
    }

    public void Dispose()
    {
        Safe.Release(_service);
        _service = null;
    }

    private static string FolderPath(string folderName) => RootFolderPath + folderName;

    private int GetFolder(string path, out ITaskFolder? folder)
    {
        ITaskService service = _service ?? throw new ObjectDisposedException(nameof(TaskSchedulerSession));
        return service.GetFolder(path, out folder);
    }

    private int GetOrCreateFolder(string folderName, out ITaskFolder? folder)
    {
        int hr = GetFolder(FolderPath(folderName), out folder);
        if (!IsNotFound(hr)) return hr;

        hr = GetFolder(RootFolderPath, out ITaskFolder? root);
        if (hr < 0 || root == null) return hr;
        try
        {
            // The folder takes the root's security descriptor
            hr = root.CreateFolder(folderName, securityDescriptor: default, out folder);
        }
        finally
        {
            Safe.Release(root);
        }

        // Created by someone else in the meantime
        return hr == ErrorAlreadyExists ? GetFolder(FolderPath(folderName), out folder) : hr;
    }

    private static int ReadTaskNames(ITaskFolder folder, out List<string> names)
    {
        names = [];
        int hr = folder.GetTasks(EnumerateHidden, out IRegisteredTaskCollection? tasks);
        if (hr < 0 || tasks == null) return hr;
        try
        {
            hr = tasks.GetCount(out int count);
            if (hr < 0) return hr;

            // The collection is one-based
            for (int index = 1; index <= count; index++)
            {
                hr = tasks.GetItem(TaskVariant.FromInt32(index), out IRegisteredTask? task);
                if (hr < 0 || task == null) return hr;
                try
                {
                    hr = task.GetName(out string? name);
                    if (hr < 0) return hr;
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
                finally
                {
                    Safe.Release(task);
                }
            }

            return 0;
        }
        finally
        {
            Safe.Release(tasks);
        }
    }
}

/// <summary>One registered task, opened through <see cref="TaskSchedulerSession.TryOpenTask"/>.</summary>
internal sealed class ScheduledTask : IDisposable
{
    private IRegisteredTask? _task;

    internal ScheduledTask(IRegisteredTask task) => _task = task;

    /// <summary>Reads the registered definition.</summary>
    public int TryGetXml(out string? xml) =>
        (_task ?? throw new ObjectDisposedException(nameof(ScheduledTask))).GetXml(out xml);

    /// <summary>Starts the task now; <paramref name="parameter"/> replaces $(Arg0) in its action.</summary>
    public int Run(string parameter)
    {
        IRegisteredTask task = _task ?? throw new ObjectDisposedException(nameof(ScheduledTask));
        TaskVariant parameters = TaskVariant.FromString(parameter);
        try
        {
            int hr = task.Run(parameters, out IntPtr runningTask);
            // The running instance is never used
            if (runningTask != IntPtr.Zero) _ = Marshal.Release(runningTask);
            return hr;
        }
        finally
        {
            parameters.Free();
        }
    }

    public void Dispose()
    {
        Safe.Release(_task);
        _task = null;
    }
}
