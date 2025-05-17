# Shared Action

A simple C# class that allows multiple concurrent requests for the same operation to run that operation just once and share the result.
It's intended to be used in systems that expect real-time results but want to avoid the waste that comes with concurrent processing of the same input.
The more concurrent requests, the greater the benefit: this solution thrives under intense load tests.

This _is not_ a cache: once an action is completed, the results are shared with everyone supplying the same input and discarded.

It's recommended to copy the source for this repository into your own and adjust as needed.

## How to Use

Note: if you load test these using a browser, you may encounter stalls due to queuing.
A purpose-built load generator tool is recommended.

### Web API controller

```C#
public record Inventory(decimal Price, int InventoryCount); // Example data object.

// The key must include every input variable.
private static readonly SharedAction<(string Sku, int WarehouseId), Inventory?> inventoryActions = new();

[HttpGet]
public async Task<Inventory?> GetAsync(string sku, int warehouseId, CancellationToken cancellationToken)
{
    return await inventoryActions.RunAsync((sku, warehouseId), async (input, cancellationToken) =>
    {
        // The "input" parameter avoids the overhead of capturing variables.
        // "GetInventoryAsync" is a placeholder for the real call to the underlying system.
        return await GetInventoryAsync(input.Sku, input.WarehouseId, cancellationToken);
    }, cancellationToken);
}
```

### ASP.NET Core Minimal API

```C#
// The key must include every input variable.
var inventoryActions = new SharedAction<(string Sku, int WarehouseId), Inventory?>();

app.MapGet("/api/realtimeinventory", async (string sku, int warehouseId, CancellationToken cancellationToken) =>
{
    var result = await inventoryActions.RunAsync((sku, warehouseId), async (input, cancellationToken) =>
    {
        // The "input" parameter avoids the overhead of capturing variables.
        // "GetInventoryAsync" is a placeholder for the real call to the underlying system.
        return await GetInventoryAsync(input.Sku, input.WarehouseId, cancellationToken);
    }, cancellationToken);

    return result;
});
```
