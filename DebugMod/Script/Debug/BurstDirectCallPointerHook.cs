using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

internal sealed class BurstDirectCallPointerHook
{
    private readonly Type _ownerType;
    private readonly string _methodName;
    private readonly Delegate _detourDelegate;
    private readonly IntPtr _detourPointer;

    private Type _burstDirectCallType;
    private FieldInfo _pointerField;
    private MethodInfo _getFunctionPointerMethod;
    private IntPtr _originalPointer;
    private bool _originalPointerCaptured;

    public BurstDirectCallPointerHook(Type ownerType, string methodName, Delegate detourDelegate)
    {
        _ownerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
        _methodName = !string.IsNullOrWhiteSpace(methodName) ? methodName : throw new ArgumentException("method name is required", nameof(methodName));
        _detourDelegate = detourDelegate ?? throw new ArgumentNullException(nameof(detourDelegate));
        _detourPointer = Marshal.GetFunctionPointerForDelegate(_detourDelegate);
    }

    public bool IsEnabled { get; private set; }

    public bool OriginalPointerCaptured => _originalPointerCaptured;

    public IntPtr CurrentPointer => _pointerField != null ? ReadPointer() : IntPtr.Zero;

    public IntPtr OriginalPointer => _originalPointer;

    public IntPtr DetourPointer => _detourPointer;

    public Type BurstDirectCallType => _burstDirectCallType;

    public bool TryEnable(out string error)
    {
        if (!TryResolve(out error))
        {
            return false;
        }

        if (!TryCaptureOriginalPointer(out error))
        {
            return false;
        }

        WritePointer(_detourPointer);
        IsEnabled = true;
        return true;
    }

    public bool TryDisable(out string error)
    {
        if (!TryResolve(out error))
        {
            return false;
        }

        WritePointer(_originalPointerCaptured ? _originalPointer : IntPtr.Zero);
        IsEnabled = false;
        return true;
    }

    public bool TryResolve(out string error)
    {
        error = null;

        if (_pointerField != null && _getFunctionPointerMethod != null)
        {
            return true;
        }

        _burstDirectCallType = _ownerType
            .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(type =>
                type.Name.IndexOf(_methodName, StringComparison.Ordinal) >= 0 &&
                type.Name.IndexOf("BurstDirectCall", StringComparison.Ordinal) >= 0);

        if (_burstDirectCallType == null)
        {
            error = $"failed to find BurstDirectCall type for {_ownerType.FullName}.{_methodName}";
            return false;
        }

        _pointerField = _burstDirectCallType.GetField("Pointer", BindingFlags.Static | BindingFlags.NonPublic);
        _getFunctionPointerMethod = _burstDirectCallType.GetMethod("GetFunctionPointer", BindingFlags.Static | BindingFlags.NonPublic);

        if (_pointerField == null || _getFunctionPointerMethod == null)
        {
            error = $"failed to resolve BurstDirectCall members on {_burstDirectCallType.FullName}";
            return false;
        }

        return true;
    }

    private bool TryCaptureOriginalPointer(out string error)
    {
        error = null;

        if (_originalPointerCaptured)
        {
            return true;
        }

        try
        {
            _originalPointer = (IntPtr)_getFunctionPointerMethod.Invoke(null, null);
            _originalPointerCaptured = true;
            return true;
        }
        catch (Exception ex)
        {
            error = $"failed to capture original Burst pointer for {_ownerType.FullName}.{_methodName}: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private IntPtr ReadPointer()
    {
        object value = _pointerField.GetValue(null);
        return value is IntPtr pointer ? pointer : IntPtr.Zero;
    }

    private void WritePointer(IntPtr pointer)
    {
        _pointerField.SetValue(null, pointer);
    }
}
