namespace SPCStar.Core.Services;

public sealed record ServiceResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static ServiceResult Ok() => new(true, []);
    public static ServiceResult Fail(params string[] errors) => new(false, errors);
    public static ServiceResult Fail(IEnumerable<string> errors) => new(false, errors.ToArray());
}

public sealed record ServiceResult<T>(bool Succeeded, T? Value, IReadOnlyList<string> Errors)
{
    public static ServiceResult<T> Ok(T value) => new(true, value, []);
    public static ServiceResult<T> Fail(params string[] errors) => new(false, default, errors);
    public static ServiceResult<T> Fail(IEnumerable<string> errors) => new(false, default, errors.ToArray());
}
