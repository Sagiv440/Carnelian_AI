using AI_Interface.Models;
using AI_Interface.Services;
using Xunit;

namespace AI_Interface.Tests;

/// <summary>
/// Tests for the <see cref="AgentToolGroup.Mcp"/> permission added for MCP tools: <see cref="AgentTools"/>
/// resolution (unrestricted vs explicit and <see cref="AgentTools.Restrict"/>), and the orchestrator's
/// <c>CapTools</c> ceiling intersecting the MCP group — a delegated specialist may use MCP tools only when
/// BOTH the lead's ceiling and the specialist's own allow-list permit them.
/// </summary>
public sealed class McpToolGroupTests
{
    [Fact]
    public void UnrestrictedAgent_AllowsMcp()
    {
        Assert.True(new AgentTools().Allows(AgentToolGroup.Mcp)); // AllowAll = true by default
    }

    [Fact]
    public void ExplicitAgent_RespectsMcpFlag()
    {
        Assert.False(new AgentTools { AllowAll = false, Mcp = false }.Allows(AgentToolGroup.Mcp));
        Assert.True(new AgentTools { AllowAll = false, Mcp = true }.Allows(AgentToolGroup.Mcp));
    }

    [Fact]
    public void Restrict_SnapshotsMcpAsAllowed()
    {
        var tools = new AgentTools(); // unrestricted
        tools.Restrict();
        Assert.False(tools.AllowAll);
        Assert.True(tools.Mcp); // the all-on snapshot keeps MCP enabled
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void CapTools_IntersectsMcp(bool leadMcp, bool specialistMcp, bool expected)
    {
        var lead = new AgentTools { AllowAll = false, Mcp = leadMcp };
        var specialist = new AgentTools { AllowAll = false, Mcp = specialistMcp };
        var capped = AgentOrchestrator.CapTools(lead, specialist);
        Assert.Equal(expected, capped.Allows(AgentToolGroup.Mcp));
    }
}
