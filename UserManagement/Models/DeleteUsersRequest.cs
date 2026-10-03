namespace UserManagement.Models;

public class DeleteUsersRequest
{
    public List<int> Ids { get; set; } = new();
}
