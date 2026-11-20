using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace GitHubBackup.App.Controls;

/// <summary>
/// A panel of the main window or of the settings editor drawn as a card: the fill, frame, corner radius and padding come from the shared
/// <c>PanelCardStyle</c>. Unlike a plain <see cref="Border"/>, it is visible to UI Automation as a group, so tests
/// and screen readers see the panel, its automation id and its bounds.
/// </summary>
public sealed class CardPanel : Border
{
    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new CardPanelAutomationPeer(this);

    /// <summary>
    /// Exposes a <see cref="CardPanel"/> as a group.
    /// </summary>
    /// <param name="owner">The card.</param>
    private sealed class CardPanelAutomationPeer(CardPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        /// <inheritdoc />
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        /// <inheritdoc />
        protected override string GetClassNameCore() => nameof(CardPanel);
    }
}
