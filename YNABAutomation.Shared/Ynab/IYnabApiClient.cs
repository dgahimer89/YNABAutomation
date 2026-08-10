namespace YNABAutomationConsole.Ynab;

public interface IYnabApiClient
{
    Task<PlansResponse> GetPlansAsync(
        GetPlansOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TransactionsResponse> GetTransactionsAsync(
        GetTransactionsOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<CategoriesResponse> GetCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<SaveTransactionsResponse> UpdateTransactionsAsync(
        IReadOnlyCollection<UpdateTransactionsRequest> transactions,
        CancellationToken cancellationToken = default);

    Task<TransactionResponse> UpdateTransactionAsync(
        UpdateTransactionCategoryRequest transaction,
        CancellationToken cancellationToken = default);
}
