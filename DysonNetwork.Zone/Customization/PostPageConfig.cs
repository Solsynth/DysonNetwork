namespace DysonNetwork.Zone.Customization;

// PostPage.Config -> filter
public class PostPageFilterConfig
{
    public List<int> Types { get; set; }
    public string? PubName { get; set; }
    public string? OrderBy { get; set; }
    public bool OrderDesc { get; set; } = true;
}

// PostPage.Config -> layout
public class PostPageLayoutConfig
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool ShowPub { get; set; } = true;
}