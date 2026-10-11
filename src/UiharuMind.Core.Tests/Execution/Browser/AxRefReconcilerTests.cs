using UiharuMind.Core.AI.Execution.Tools.Browser;

namespace UiharuMind.Core.Tests.Execution.Browser;

/// <summary>
/// 同一条回复里前面的步骤改了页面：结构没变编号照用，变了只认得回独一份的元素，认不准就拒
/// </summary>
public class AxRefReconcilerTests
{
    private static Dictionary<string, AxNode> Refs(params AxNode[] nodes) =>
        nodes.Select((node, i) => (node, i)).ToDictionary(pair => "e" + (pair.i + 1), pair => pair.node);

    [Fact]
    public void UnchangedLayout_KeepsRefsEvenWhenValuesChanged()
    {
        Dictionary<string, AxNode> seen = Refs(new AxNode("textbox", ""), new AxNode("textbox", ""));
        AxNode filled = new("textbox", "") { Value = "Alice" };
        Dictionary<string, AxNode> current = Refs(filled, new AxNode("textbox", ""));

        Assert.Same(filled, AxRefReconciler.Find(seen, current, "e1"));
    }

    [Fact]
    public void ShiftedLayout_FollowsAUniqueElementToItsNewRef()
    {
        AxNode submit = new("button", "提交订单");
        Dictionary<string, AxNode> seen = Refs(new AxNode("button", "展开"), new AxNode("button", "提交订单"));
        Dictionary<string, AxNode> current = Refs(new AxNode("button", "收起"), new AxNode("option", "加急"), submit);

        Assert.Same(submit, AxRefReconciler.Find(seen, current, "e2"));
    }

    [Fact]
    public void ShiftedLayout_RefusesDuplicatesInsteadOfHittingTheWrongRow()
    {
        Dictionary<string, AxNode> seen = Refs(new AxNode("button", "删除"), new AxNode("button", "删除"),
            new AxNode("button", "删除"));
        Dictionary<string, AxNode> current = Refs(new AxNode("button", "删除"), new AxNode("button", "删除"));

        Assert.Throws<BrowserActionException>(() => AxRefReconciler.Find(seen, current, "e2"));
    }

    [Fact]
    public void ShiftedLayout_ReportsAnElementThatIsGone()
    {
        Dictionary<string, AxNode> seen = Refs(new AxNode("link", "下一页"), new AxNode("button", "确认"));
        Dictionary<string, AxNode> current = Refs(new AxNode("link", "上一页"));

        BrowserActionException error =
            Assert.Throws<BrowserActionException>(() => AxRefReconciler.Find(seen, current, "e2"));
        Assert.Contains("no longer on the page", error.Message);
    }

    [Fact]
    public void RefTheModelNeverSaw_IsNotFound()
    {
        Dictionary<string, AxNode> seen = Refs(new AxNode("button", "确认"));
        Dictionary<string, AxNode> current = Refs(new AxNode("button", "确认"), new AxNode("button", "取消"));

        Assert.Null(AxRefReconciler.Find(seen, current, "e2"));
    }
}
