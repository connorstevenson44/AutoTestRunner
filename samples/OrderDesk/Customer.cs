namespace OrderDesk;

/// <summary>A customer, as the Customers tab shows them: their account, and how many orders they have placed.</summary>
public sealed record Customer(string Account, string Name, string Email, int Orders);
