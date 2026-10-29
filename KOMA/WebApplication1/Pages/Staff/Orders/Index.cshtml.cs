using KOMA.Data;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;

namespace KOMA.Pages.Staff.Orders;

[Authorize(Roles = "Staff,Administrator")]
public class IndexModel(
    ApplicationDbContext db,
    OrderWorkflowService workflow)
    : OrderBoardModel(db, workflow)
{
    public override bool Kitchen => false;
}