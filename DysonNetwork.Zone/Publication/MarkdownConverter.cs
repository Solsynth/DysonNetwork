using Markdig;

namespace DysonNetwork.Zone.Publication;

public class MarkdownConverter
{
    private readonly MarkdownPipeline _pipelineSoftBreak = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public string ToHtml(string markdown, bool softBreaks = true)
    {
        var procMarkdown = markdown.Replace("solian://files/", "/drive/files/");
        return string.IsNullOrEmpty(procMarkdown)
            ? string.Empty
            : Markdown.ToHtml(procMarkdown, softBreaks ? _pipelineSoftBreak : _pipeline);
    }
}
