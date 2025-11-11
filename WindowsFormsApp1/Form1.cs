using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        // ======= CONFIG =======
        private const string API_KEY = "AIzaSyAmG3JUlC4L4aTWUtrFKcOJPsiI-_icbow";
        private const int MAX_RESULTS = 25;
        private const string REGION_CODE = "VN";
        // =======================

        private HttpClient httpClient = new HttpClient();
        private List<YTVideo> videos = new List<YTVideo>();

        public Form1()
        {
            InitializeComponent(); // gọi method trong Designer
            InitializeDataGridColumns();
            this.Load += Form1_Load;
        }

        private void InitializeDataGridColumns()
        {
           
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Title" });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Channel", HeaderText = "Channel" });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Views", HeaderText = "Views", FillWeight = 20 });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "VideoId", HeaderText = "VideoId", Visible = false });
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            lblStatus.Text = "Initializing WebView...";
            try
            {
                await webView.EnsureCoreWebView2Async();
                lblStatus.Text = "Ready";
            }
            catch
            {
                lblStatus.Text = "WebView2 not available - will open browser on Play";
            }

            // Optionally auto load trending
            await LoadTrendingAsync();
        }

        #region Data model
        private class YTVideo
        {
            public string VideoId { get; set; }
            public string Title { get; set; }
            public string ChannelTitle { get; set; }
            public long ViewCount { get; set; }
            public long LikeCount { get; set; }
            public string Description { get; set; }
            public DateTime PublishedAt { get; set; }
            public string ThumbnailUrl { get; set; }
        }
        #endregion

        #region API logic
        private async Task LoadTrendingAsync()
        {   
            try
            {
                SetUiBusy("Loading trending...");
                videos.Clear();
                dgvVideos.Rows.Clear();

                string url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&chart=mostPopular&regionCode={REGION_CODE}&maxResults={MAX_RESULTS}&key={API_KEY}";
                var resp = await httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Error fetching trending: {resp.StatusCode}\n{await resp.Content.ReadAsStringAsync()}");
                    SetUiReady("Error loading trending");
                    return;
                }

                string json = await resp.Content.ReadAsStringAsync();
                var j = JObject.Parse(json);
                var items = (JArray)j["items"];

                foreach (var it in items)
                {
                    var id = (string)it["id"];
                    var snippet = it["snippet"];
                    var stats = it["statistics"];
                    var vid = new YTVideo
                    {
                        VideoId = id,
                        Title = (string)snippet["title"] ?? "",
                        ChannelTitle = (string)snippet["channelTitle"] ?? "",
                        ViewCount = stats != null && stats["viewCount"] != null ? (long)(stats.Value<long?>("viewCount") ?? 0) : 0,
                        LikeCount = stats != null && stats["likeCount"] != null ? (long)(stats.Value<long?>("likeCount") ?? 0) : 0,
                        Description = (string)snippet["description"] ?? "",
                        PublishedAt = snippet["publishedAt"] != null ? DateTime.Parse((string)snippet["publishedAt"]) : DateTime.MinValue,
                        ThumbnailUrl = (string)snippet["thumbnails"]?["medium"]?["url"] ?? (string)snippet["thumbnails"]?["default"]?["url"]
                    };
                    videos.Add(vid);

                    dgvVideos.Rows.Add(
                        vid.Title,
                        vid.ChannelTitle,
                        vid.ViewCount.ToString("N0"),
                        vid.VideoId,
                        vid.PublishedAt.ToString("yyyy-MM-dd"),
                        vid.Description.Length > 100 ? vid.Description.Substring(0, 100) + "..." : vid.Description,
                        vid.LikeCount.ToString("N0")
                    );
                }

         

                SetUiReady($"Loaded trending: {videos.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                SetUiReady("Error");
            }
        }


        private async Task SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) { MessageBox.Show("Please enter search terms."); return; }

            try
            {
                SetUiBusy($"Searching \"{query}\"...");
                videos.Clear();
                dgvVideos.Rows.Clear();

                string urlSearch = $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults={MAX_RESULTS}&q={Uri.EscapeDataString(query)}&regionCode={REGION_CODE}&key={API_KEY}";
                var resp = await httpClient.GetAsync(urlSearch);
                if (!resp.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Search error: {resp.StatusCode}\n{await resp.Content.ReadAsStringAsync()}");
                    SetUiReady("Search error");
                    return;
                }

                string jsonSearch = await resp.Content.ReadAsStringAsync();
                var jSearch = JObject.Parse(jsonSearch);
                var items = (JArray)jSearch["items"];
                var ids = items.Select(it => (string)it["id"]?["videoId"]).Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (ids.Count == 0) { SetUiReady("No results"); return; }

                string idsParam = string.Join(",", ids);
                string urlVideos = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&id={idsParam}&key={API_KEY}";
                var resp2 = await httpClient.GetAsync(urlVideos);
                if (!resp2.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Videos error: {resp2.StatusCode}\n{await resp2.Content.ReadAsStringAsync()}");
                    SetUiReady("Error");
                    return;
                }

                string jsonVideos = await resp2.Content.ReadAsStringAsync();
                var jVideos = JObject.Parse(jsonVideos);
                var items2 = (JArray)jVideos["items"];
                foreach (var it in items2)
                {
                    var id = (string)it["id"];
                    var snippet = it["snippet"];
                    var stats = it["statistics"];
                    var vid = new YTVideo
                    {
                        VideoId = id,
                        Title = (string)snippet["title"] ?? "",
                        ChannelTitle = (string)snippet["channelTitle"] ?? "",
                        ViewCount = stats != null && stats["viewCount"] != null ? (long)(stats.Value<long?>("viewCount") ?? 0) : 0,
                        ThumbnailUrl = (string)snippet["thumbnails"]?["medium"]?["url"] ?? (string)snippet["thumbnails"]?["default"]?["url"]
                    };
                    videos.Add(vid);
                    dgvVideos.Rows.Add(vid.Title, vid.ChannelTitle, vid.ViewCount.ToString("N0"), vid.VideoId);
                }

                SetUiReady($"Search results: {videos.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                SetUiReady("Error");
            }
        }
        #endregion

        #region UI Helpers & Handlers
        private void SetUiBusy(string text)
        {
            lblStatus.Text = text;
            btnTrending.Enabled = btnSearch.Enabled = btnPlay.Enabled = false;
        }

        private void SetUiReady(string text)
        {
            lblStatus.Text = text;
            btnTrending.Enabled = btnSearch.Enabled = btnPlay.Enabled = true;
        }

        private async void BtnTrending_Click(object sender, EventArgs e)
        {
            await LoadTrendingAsync();
        }

        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            await SearchAsync(txtSearch.Text.Trim());
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            PlaySelected();
        }

        private void DgvVideos_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0) PlaySelected();
        }

        private async void DgvVideos_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvVideos.SelectedRows.Count == 0) return;
            var videoId = dgvVideos.SelectedRows[0].Cells["VideoId"].Value?.ToString();
            if (string.IsNullOrEmpty(videoId)) return;

            var v = videos.FirstOrDefault(x => x.VideoId == videoId);
            if (v == null) return;

            lblStatus.Text = $"Selected: {v.Title}";
            // load thumbnail
            if (!string.IsNullOrEmpty(v.ThumbnailUrl))
            {
                try
                {
                    // Download the thumbnail stream asynchronously
                    var networkStream = await httpClient.GetStreamAsync(v.ThumbnailUrl);

                    // Use explicit using statements (compatible with C# 7.3)
                    using (var s = networkStream)
                    using (var ms = new System.IO.MemoryStream())
                    {
                        // Copy to memory so we can dispose the network stream immediately
                        await s.CopyToAsync(ms);
                        ms.Position = 0;

                        // Replace the picture box image safely
                        picThumbnail.Image?.Dispose();

                        using (var img = System.Drawing.Image.FromStream(ms))
                        {
                            // Create a detached copy so the MemoryStream can be disposed
                            picThumbnail.Image = new System.Drawing.Bitmap(img);
                        }
                    }
                }
                catch
                {
                    picThumbnail.Image = null;
                }
            }
            else picThumbnail.Image = null;
        }

        private void PlaySelected()
        {
            if (dgvVideos.SelectedRows.Count == 0) { MessageBox.Show("Please select a video first."); return; }
            string id = dgvVideos.SelectedRows[0].Cells["VideoId"].Value?.ToString();
            if (string.IsNullOrEmpty(id)) return;
            string url = $"https://www.youtube.com/watch?v={id}";

            if (webView?.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate(url);
            }
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot open browser: " + ex.Message);
                }
            }
        }
        #endregion    
    }
}
