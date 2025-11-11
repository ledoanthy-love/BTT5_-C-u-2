using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace WindowsFormsApp1
{
    public partial class Youtube : Form
    {
        private const string API_KEY = "AIzaSyAmG3JUlC4L4aTWUtrFKcOJPsiI-_icbow"; // <-- thay API key của bạn vào đây
        private const string REGION_CODE = "VN";
        private const int MAX_RESULTS = 25;
        // =======================

        private HttpClient httpClient = new HttpClient();
        private List<YTVideo> videos = new List<YTVideo>();

        //Database
        private string connectionString = "Data Source=.\\LAPTOP-SMF5VRH9;Initial Catalog=YTB;Integrated Security=True";
        // Đối tượng kết nối dữ liệu
        SqlConnection conn = null;

        public Youtube()
        {
            InitializeComponent();
            InitializeDataGridColumns();
            this.Load += Form1_Load;
        }
        private void InitializeDataGridColumns()
        {
            dgvVideos.Columns.Clear();
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Title" });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Channel", HeaderText = "Channel" });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Views", HeaderText = "Views", FillWeight = 20 });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "VideoId", HeaderText = "VideoId", Visible = false });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "AirTime", HeaderText = "AirTime", Visible = true });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "Description", Visible = true });
            dgvVideos.Columns.Add(new DataGridViewTextBoxColumn { Name = "LikeCount", HeaderText = "LikeCount", Visible = true });
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            lblStatus.Text = "Initializing WebView...";
            try
            {
                // 🔹 Khởi tạo WebView2 thủ công
                webView = new WebView2();
                webView.Dock = DockStyle.Fill;
                this.Controls.Add(webView);

                // 🔹 Khởi tạo lõi WebView2
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
            public string ThumbnailUrl { get; set; }

            public DateTime? AirTime { get; set; }
            public string Description { get; set; }
            public long LikeCount { get; set; }
        }
        #endregion

        #region API logic
        private async Task LoadTrendingAsync()
        {
            try
            {
                SetUiBusy("Checking local database...");

                DateTime today = DateTime.Today;
                var existing = GetTrendingFromDb(today);

                if (existing.Any())
                {
                    // Đã có dữ liệu trending hôm nay
                    videos = existing;
                    dgvVideos.Rows.Clear();

                    foreach (var vid in videos)
                    {
                        dgvVideos.Rows.Add(
                            vid.Title,
                            vid.ChannelTitle,
                            vid.ViewCount.ToString("N0"),
                            vid.VideoId,
                            vid.AirTime?.ToString("HH:mm dd-MM-yyyy") ?? "",
                            vid.Description,
                            vid.LikeCount.ToString()
                        );
                    }

                    SetUiReady($"Loaded trending from DB: {videos.Count}");
                    return;
                }

                // Chưa có -> gọi API
                SetUiBusy("Loading trending from YouTube...");
                videos.Clear();
                dgvVideos.Rows.Clear();

                string url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&chart=mostPopular&regionCode ={REGION_CODE}&maxResults={MAX_RESULTS}&key={API_KEY}";
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

                    DateTime? published = null;
                    if (DateTime.TryParse((string)snippet["publishedAt"], out DateTime d))
                        published = d;

                    var vid = new YTVideo
                    {
                        VideoId = id,
                        Title = (string)snippet["title"] ?? "",
                        ChannelTitle = (string)snippet["channelTitle"] ?? "",
                        ViewCount = stats?.Value<long?>("viewCount") ?? 0,
                        ThumbnailUrl = (string)snippet["thumbnails"]?["medium"]?["url"] ?? "",
                        AirTime = published,
                        Description = (string)snippet["description"] ?? "",
                        LikeCount = stats?.Value<long?>("likeCount") ?? 0
                    };

                    videos.Add(vid);
                    dgvVideos.Rows.Add(
                        vid.Title,
                        vid.ChannelTitle,
                        vid.ViewCount.ToString("N0"),
                        vid.VideoId,
                        vid.AirTime?.ToString("HH:mm dd-MM-yyyy") ?? "",
                        vid.Description,
                        vid.LikeCount.ToString()
                    );
                }

                // Lưu vào CSDL
                SaveTrendingToDb(videos, today);
                SetUiReady($"Loaded trending from API and saved ({videos.Count})");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                SetUiReady("Error");
            }
        }
  

        private void SaveTrendingToDb(List<YTVideo> list, DateTime date)
        {
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                foreach (var v in list)
                {
                    string sql = @"INSERT INTO TrendingVideos
                        (VideoId, Title, ChannelTitle, ViewCount, AirTime, Description, LikeCount, Ngay)
                        VALUES (@VideoId, @Title, @ChannelTitle, @ViewCount, @AirTime, @Description, @LikeCount, @Ngay)";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@VideoId", v.VideoId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Title", v.Title ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ChannelTitle", v.ChannelTitle ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ViewCount", v.ViewCount);
                        // Correctly handle nullable DateTime for AirTime
                        cmd.Parameters.AddWithValue("@AirTime", v.AirTime.HasValue ? (object)v.AirTime.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@Description", v.Description ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@LikeCount", v.LikeCount);
                        cmd.Parameters.AddWithValue("@Ngay", date);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private List<YTVideo> GetTrendingFromDb(DateTime date)
        {
            var list = new List<YTVideo>();
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = "SELECT * FROM TrendingVideos WHERE Ngay = @Ngay";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Ngay", date);
                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            list.Add(new YTVideo
                            {
                                VideoId = rd["VideoId"].ToString(),
                                Title = rd["Title"].ToString(),
                                ChannelTitle = rd["ChannelTitle"].ToString(),
                                ViewCount = Convert.ToInt64(rd["ViewCount"]),
                                AirTime = rd["AirTime"] == DBNull.Value ? null : (DateTime?)rd["AirTime"],
                                Description = rd["Description"].ToString(),
                                LikeCount = Convert.ToInt64(rd["LikeCount"])
                            });
                        }
                    }
                }
            }
            return list;
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
                    // khi tạo YTVideo
                    DateTime? parsedAir = null;
                    var publishedAtStr = (string)snippet["publishedAt"];
                    if (!string.IsNullOrEmpty(publishedAtStr))
                    {
                        if (DateTime.TryParse(publishedAtStr, out DateTime dt))
                        {
                            parsedAir = dt.ToLocalTime(); // hoặc giữ nguyên tuỳ bạn
                        }
                    }
                    var vid = new YTVideo
                    {
                        VideoId = id,
                        Title = (string)snippet["title"] ?? "",
                        ChannelTitle = (string)snippet["channelTitle"] ?? "",
                        ViewCount = stats != null && stats["viewCount"] != null ? (long)(stats.Value<long?>("viewCount") ?? 0) : 0,
                        ThumbnailUrl = (string)snippet["thumbnails"]?["medium"]?["url"] ?? (string)snippet["thumbnails"]?["default"]?["url"],
                        AirTime = parsedAir,
                        Description = (string)snippet["description"] ?? "",
                        LikeCount = stats != null && stats["likeCount"] != null ? (long)(stats.Value<long?>("likeCount") ?? 0) : 0
                    };
                    videos.Add(vid);
                    dgvVideos.Rows.Add(vid.Title, vid.ChannelTitle, vid.ViewCount.ToString("N0"), vid.VideoId, vid.AirTime, vid.Description, vid.LikeCount);
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
                    // Use classic using blocks (C# 7.3). Await is allowed in the using initializer.
                    using (var s = await httpClient.GetStreamAsync(v.ThumbnailUrl))
                    {
                        // Create a temporary Image from the stream, then clone it via new Bitmap(image)
                        // so the resulting image does not depend on the stream lifetime.
                        using (var img = System.Drawing.Image.FromStream(s))
                        {
                            // Dispose previous image if any
                            if (picThumbnail.Image != null)
                            {
                                picThumbnail.Image.Dispose();
                                picThumbnail.Image = null;
                            }

                            // Clone into a new Bitmap which is independent of the stream and the temporary image
                            picThumbnail.Image = new System.Drawing.Bitmap(img);
                        }
                    }
                }
                catch
                {
                    // On any failure, clear the picture
                    if (picThumbnail.Image != null)
                    {
                        picThumbnail.Image.Dispose();
                        picThumbnail.Image = null;
                    }
                }
            }
            else
            {
                if (picThumbnail.Image != null)
                {
                    picThumbnail.Image.Dispose();
                    picThumbnail.Image = null;
                }
            }
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
