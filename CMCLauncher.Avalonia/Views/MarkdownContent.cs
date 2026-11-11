using Avalonia.Controls;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CMCLauncher.Avalonia.Views;

public sealed partial class MainWindow
{
    Control MarkdownContent(string markdown)
    {
        var panel = Stack();
        void Blocks(ContainerBlock blocks)
        {
            foreach (var block in blocks)
            {
                if (block is CodeBlock code)
                    panel.Children.Add(Card(T(code.Lines.ToString(), 12)));
                else if (block is LeafBlock leaf && leaf.Inline != null)
                {
                    panel.Children.Add(
                        T(
                            Plain(leaf.Inline),
                            block is HeadingBlock heading
                                ? Math.Max(16, 26 - heading.Level * 2)
                                : 14,
                            block is HeadingBlock
                        )
                    );
                    foreach (
                        var link in Links(leaf.Inline)
                            .Where(link =>
                                !link.IsImage
                                && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
                                && uri.Scheme == "https"
                            )
                    )
                    {
                        var url = link.Url!;
                        panel.Children.Add(
                            Btn(
                                Plain(link),
                                () =>
                                {
                                    Open(url);
                                    return Task.CompletedTask;
                                }
                            )
                        );
                    }
                }
                else if (block is ContainerBlock container)
                    Blocks(container);
            }
        }
        // Raw HTML remains non-executable; remote images are loaded only by the validated cover loader.
        Blocks(Markdown.Parse(markdown ?? ""));
        return panel;
    }

    static string Plain(ContainerInline container) =>
        string.Concat(
            container.Select(inline =>
                inline switch
                {
                    LiteralInline literal => literal.Content.ToString(),
                    CodeInline code => code.Content,
                    LineBreakInline => "\n",
                    ContainerInline nested => Plain(nested),
                    _ => "",
                }
            )
        );

    static IEnumerable<LinkInline> Links(ContainerInline container)
    {
        foreach (var inline in container)
        {
            if (inline is LinkInline link)
                yield return link;
            if (inline is ContainerInline nested)
                foreach (var child in Links(nested))
                    yield return child;
        }
    }
}
