namespace Dst.Core.Helpers;

[GenerateSerializer]
public sealed record GrainResult<TResult>
{
    public static GrainResult<TResult> Success(TResult value) => new(true, value, null);
    public static GrainResult<TResult> Failure(string errorMessage) => new(false, default!, errorMessage);

    [Id(0)]
    public bool IsSuccess { get; }
    [Id(1)]
    public TResult Value { get; }
    [Id(2)]
    public string? ErrorMessage { get; }

    private GrainResult(bool isSuccess, TResult value, string? errorMessage)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }
}
