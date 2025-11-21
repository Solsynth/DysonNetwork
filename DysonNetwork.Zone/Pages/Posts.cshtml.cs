
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Zone.Pages
{
    public class PostsModel : PageModel
    {
        private readonly PostService.PostServiceClient _postClient;

        public PostsModel(PostService.PostServiceClient postClient)
        {
            _postClient = postClient;
        }

        public List<SnPost> Posts { get; set; } = new();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; } = 10;
        public int TotalPages => (int)System.Math.Ceiling(TotalCount / (double)PageSize);

        public async Task OnGetAsync(int currentPage = 1)
        {
            CurrentPage = currentPage;

            var request = new ListPostsRequest
            {
                OrderBy = "date",
                OrderDesc = true,
                PageSize = PageSize,
                PageToken = ((CurrentPage - 1) * PageSize).ToString()
            };

            var response = await _postClient.ListPostsAsync(request);

            if (response?.Posts != null)
            {
                Posts = response.Posts.Select(SnPost.FromProtoValue).ToList();
                TotalCount = response.TotalSize;
            }
        }
    }
}
