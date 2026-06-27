using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Data;
using System.Linq;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents; 
using System.Windows.Media;
using MySql.Data.MySqlClient;

namespace CyberGuardianSA_Part3
{
    public partial class MainWindow : Window
    {
        // ---------- Database connection ----------
        private readonly string connectionString = "Server=localhost;Database=CyberGuardian_db;Uid=root;Pwd=Otlotleng@18;";

        // ---------- Chatbot logic ----------
        private readonly ChatbotLogic chatbot;
        private SpeechSynthesizer synthesizer;
        private bool voiceEnabled = true;

        // ---------- Task management ----------
        private ObservableCollection<TaskItem> tasks = new ObservableCollection<TaskItem>();

        // -------- -- Quiz ----------
        private readonly QuizManager quizManager;
        private int currentQuestionIndex = -1;
        private int quizScore = 0;
        private bool quizActive = false;

        // ---------- Activity log (in-memory) ----------
        private List<string> activityLog = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            chatbot = new ChatbotLogic();
            quizManager = new QuizManager();
            taskListBox.ItemsSource = tasks;

            // Init speech
            try
            {
                synthesizer = new SpeechSynthesizer();
                synthesizer.SetOutputToDefaultAudioDevice();
            }
            catch { voiceEnabled = false; }

            // Load tasks from DB
            LoadTasksFromDB();

            // Welcome message
            string welcome = "Hello! I am CyberGuardian, your final cybersecurity assistant. I can help with tips, tasks, quizzes, and more.";
            AppendBotMessage(welcome);
            Speak(welcome);
            LogActivity("Application started", "System");

            inputTextBox.KeyDown += InputTextBox_KeyDown;
        }

        // ================== CHAT ==================
        private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { ProcessUserInput(); e.Handled = true; }
        }
        private void SendButton_Click(object sender, RoutedEventArgs e) => ProcessUserInput();
        private void QuickReply_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn != null) { inputTextBox.Text = btn.Content.ToString(); ProcessUserInput(); }
        }

        private void ProcessUserInput()
        {
            string userMessage = inputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(userMessage)) return;

            AppendUserMessage(userMessage);
            LogActivity($"User: {userMessage}", "Chat");
            inputTextBox.Clear();

            // ---- NLP: Detect intent and extract entities ----
            string intent = DetectIntent(userMessage);
            string entity = ExtractEntity(userMessage);

            // ---- Handle specific intents ----
            if (intent == "task" || intent == "reminder")
            {
                HandleTaskIntent(userMessage);
                return;
            }
            if (intent == "quiz")
            {
                StartQuiz();
                string reply = "🎮 Starting cybersecurity quiz! Go to the Quiz tab or keep asking questions.";
                AppendBotMessage(reply);
                Speak(reply);
                LogActivity("Quiz started via chat", "Quiz");
                return;
            }
            if (intent == "log")
            {
                ShowActivityLog();
                return;
            }

            // ---- Fallback to chatbot logic (Part 1 & 2) ----
            bool topicChanged;
            string botReply = chatbot.GetResponse(userMessage, out topicChanged);

            // Personalise with name
            if (!string.IsNullOrEmpty(chatbot.UserName) && !botReply.Contains(chatbot.UserName))
            {
                if (topicChanged && !botReply.StartsWith("As someone interested"))
                    botReply = $"{chatbot.UserName}, {botReply}";
            }

            AppendBotMessage(botReply);
            Speak(botReply);
            LogActivity($"Bot response: {botReply.Substring(0, Math.Min(50, botReply.Length))}...", "Chat");
        }

        // ================== NLP SIMULATION ==================
        private string DetectIntent(string input)
        {
            string lower = input.ToLower();
            if (lower.Contains("task") || lower.Contains("reminder") || lower.Contains("todo") || lower.Contains("add") && lower.Contains("task"))
                return "task";
            if (lower.Contains("quiz") || lower.Contains("game") || lower.Contains("play") || lower.Contains("question"))
                return "quiz";
            if (lower.Contains("log") || lower.Contains("history") || lower.Contains("activity") || lower.Contains("what have you done"))
                return "log";
            return "general";
        }

        private string ExtractEntity(string input)
        {
            string lower = input.ToLower();
            // Try to extract task description after "add task", "remind me", etc.
            string[] prefixes = { "add task", "create task", "remind me", "set reminder", "task" };
            foreach (var pref in prefixes)
            {
                int idx = lower.IndexOf(pref);
                if (idx >= 0)
                {
                    string rest = input.Substring(idx + pref.Length).Trim();
                    if (!string.IsNullOrEmpty(rest)) return rest;
                }
            }
            // If no prefix, return whole input
            return input;
        }

        private void HandleTaskIntent(string userInput)
        {
            string description = ExtractEntity(userInput);
            if (string.IsNullOrEmpty(description))
            {
                AppendBotMessage("I didn't catch the task description. Please tell me what task you'd like to add.");
                return;
            }

            // Ask if they want a reminder
            string reply = $"Task added: '{description}'. Would you like to set a reminder? (e.g., 'Remind me in 3 days' or 'yes')";
            AppendBotMessage(reply);
            Speak(reply);

            // For simplicity, we'll prompt in the same message; but we need to capture next input.
            // We'll store a state: pendingReminderTask = description
            // For demo, we'll directly ask and then wait for user's next input; but here we'll just add task without reminder.
   
            // Let's just add task with no reminder.
            AddTaskToDB(description, "", null);
            LogActivity($"Task added via NLP: {description}", "Task");
        }

        // ================== TASK MANAGEMENT (MySQL) ==================
        private void LoadTasksFromDB()
        {
            tasks.Clear();
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "SELECT id, title, description, reminder_date, is_completed FROM tasks ORDER BY created_at DESC";
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tasks.Add(new TaskItem
                            {
                                Id = reader.GetInt32("id"),
                                Title = reader.GetString("title"),
                                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString("description"),
                                ReminderDate = reader.IsDBNull(reader.GetOrdinal("reminder_date")) ? (DateTime?)null : reader.GetDateTime("reminder_date"),
                                IsCompleted = reader.GetBoolean("is_completed")
                            });
                        }
                    }
                }
                LogActivity($"Loaded {tasks.Count} tasks from database", "Task");
            }
            catch (Exception ex)
            {
                AppendBotMessage($"⚠️ Database error: {ex.Message}");
                LogActivity($"DB error: {ex.Message}", "Error");
            }
        }

        private void AddTaskToDB(string title, string description, DateTime? reminder)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "INSERT INTO tasks (title, description, reminder_date) VALUES (@title, @desc, @reminder)";
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@desc", (object)description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@reminder", (object)reminder ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                LoadTasksFromDB(); // refresh
                LogActivity($"Task added: {title}", "Task");
            }
            catch (Exception ex)
            {
                AppendBotMessage($"⚠️ Error adding task: {ex.Message}");
                LogActivity($"Add task error: {ex.Message}", "Error");
            }
        }

        private void UpdateTaskCompletion(int id, bool completed)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "UPDATE tasks SET is_completed = @comp WHERE id = @id";
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@comp", completed);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                LoadTasksFromDB();
                LogActivity($"Task {id} marked as {(completed ? "completed" : "incomplete")}", "Task");
            }
            catch (Exception ex)
            {
                AppendBotMessage($"⚠️ Error updating task: {ex.Message}");
                LogActivity($"Update task error: {ex.Message}", "Error");
            }
        }

        private void DeleteTaskFromDB(int id)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = "DELETE FROM tasks WHERE id = @id";
                    MySqlCommand cmd = new MySqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                LoadTasksFromDB();
                LogActivity($"Task {id} deleted", "Task");
            }
            catch (Exception ex)
            {
                AppendBotMessage($"⚠️ Error deleting task: {ex.Message}");
                LogActivity($"Delete task error: {ex.Message}", "Error");
            }
        }

        // GUI event handlers for tasks
        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            string title = taskTitleBox.Text.Trim();
            string desc = taskDescBox.Text.Trim();
            DateTime? reminder = taskReminderPicker.SelectedDate;
            if (!string.IsNullOrEmpty(title))
            {
                AddTaskToDB(title, desc, reminder);
                taskTitleBox.Clear();
                taskDescBox.Clear();
                taskReminderPicker.SelectedDate = null;
                AppendBotMessage($"✅ Task added: '{title}'");
                Speak("Task added.");
            }
        }

        private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (taskListBox.SelectedItem is TaskItem selected)
            {
                DeleteTaskFromDB(selected.Id);
                AppendBotMessage($"🗑️ Task '{selected.Title}' deleted.");
                Speak("Task deleted.");
            }
        }

        private void RefreshTasksButton_Click(object sender, RoutedEventArgs e) => LoadTasksFromDB();

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox cb = sender as CheckBox;
            if (cb?.DataContext is TaskItem task)
            {
                UpdateTaskCompletion(task.Id, task.IsCompleted);
            }
        }

        // ================== QUIZ ==================
        private void StartQuizButton_Click(object sender, RoutedEventArgs e) => StartQuiz();

        private void StartQuiz()
        {
            quizActive = true;
            currentQuestionIndex = 0;
            quizScore = 0;
            UpdateScoreDisplay();
            ShowQuestion();
            startQuizButton.Content = "Next Question";
            LogActivity("Quiz started", "Quiz");
        }

        private void ShowQuestion()
        {
            if (currentQuestionIndex >= quizManager.Questions.Count)
            {
                FinishQuiz();
                return;
            }

            var q = quizManager.Questions[currentQuestionIndex];
            questionDisplay.Text = $"Q{currentQuestionIndex + 1}: {q.Question}";

            answersPanel.Children.Clear();
            for (int i = 0; i < q.Options.Count; i++)
            {
                int optionIndex = i;
                Button btn = new Button
                {
                    Content = q.Options[i],
                    Tag = optionIndex,
                    Width = 300,
                    Height = 30,
                    Margin = new Thickness(0, 3, 0, 3),
                    Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x5F, 0x8A)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                btn.Click += QuizAnswer_Click;
                answersPanel.Children.Add(btn);
            }
        }

        private void QuizAnswer_Click(object sender, RoutedEventArgs e)
        {
            if (!quizActive) return;

            Button btn = sender as Button;
            int selectedIndex = (int)btn.Tag;
            var q = quizManager.Questions[currentQuestionIndex];

            // Highlight correct/incorrect
            foreach (Button b in answersPanel.Children)
            {
                int idx = (int)b.Tag;
                if (idx == q.CorrectIndex)
                    b.Background = new SolidColorBrush(Colors.Green);
                else if (idx == selectedIndex && idx != q.CorrectIndex)
                    b.Background = new SolidColorBrush(Colors.Red);
                b.IsEnabled = false;
            }

            if (selectedIndex == q.CorrectIndex)
            {
                quizScore++;
                LogActivity($"Quiz: Correct answer for Q{currentQuestionIndex + 1}", "Quiz");
            }
            else
            {
                LogActivity($"Quiz: Incorrect answer for Q{currentQuestionIndex + 1}", "Quiz");
            }

            UpdateScoreDisplay();

            // Move to next after 2 sec
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.Tick += (s, args) => { timer.Stop(); currentQuestionIndex++; ShowQuestion(); };
            timer.Start();
        }

        private void FinishQuiz()
        {
            quizActive = false;
            string feedback = quizScore >= 10 ? "🏆 Excellent! You're a cybersecurity pro!" :
                              quizScore >= 7 ? "👍 Good job! Keep learning!" :
                              "📚 Keep learning to stay safe online!";
            questionDisplay.Text = $"🏁 Quiz Complete! Score: {quizScore}/{quizManager.Questions.Count}\n{feedback}";
            answersPanel.Children.Clear();
            startQuizButton.Content = "🔄 Play Again";
            LogActivity($"Quiz finished. Score: {quizScore}/{quizManager.Questions.Count}. {feedback}", "Quiz");
        }

        private void ResetQuizButton_Click(object sender, RoutedEventArgs e)
        {
            currentQuestionIndex = -1;
            quizScore = 0;
            quizActive = false;
            questionDisplay.Text = "Click 'Start Quiz' to begin!";
            answersPanel.Children.Clear();
            startQuizButton.Content = "🚀 Start Quiz";
            UpdateScoreDisplay();
            LogActivity("Quiz reset", "Quiz");
        }

        private void UpdateScoreDisplay() => scoreDisplay.Text = $"Score: {quizScore}/{quizManager.Questions.Count}";

        // ================== ACTIVITY LOG ==================
        private void LogActivity(string message, string category)
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message}";
            activityLog.Add(entry);
            UpdateLogDisplay();
        }

        private void UpdateLogDisplay()
        {
            logListBox.Items.Clear();
            // Show last 10 entries
            var lastEntries = activityLog.Skip(Math.Max(0, activityLog.Count - 10));
            foreach (var entry in lastEntries)
                logListBox.Items.Add(entry);
        }

        private void ShowActivityLog()
        {
            string logSummary = "📋 Recent actions:\n";
            var lastEntries = activityLog.Skip(Math.Max(0, activityLog.Count - 10));
            int i = 1;
            foreach (var entry in lastEntries)
            {
                logSummary += $"{i}. {entry}\n";
                i++;
            }
            AppendBotMessage(logSummary);
            Speak("Here is your activity log.");
            LogActivity("User requested activity log", "Log");
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            activityLog.Clear();
            UpdateLogDisplay();
            LogActivity("Log cleared", "System");
        }

        private void ExportLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = $"CyberGuardian_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                System.IO.File.WriteAllLines(path, activityLog);
                MessageBox.Show($"Log exported to: {path}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                LogActivity($"Log exported to {path}", "System");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowLogButton_Click(object sender, RoutedEventArgs e) => ShowActivityLog();

        // ================== VOICE & UI HELPERS ==================
        private void AppendUserMessage(string message)
        {
            Run run = new Run($"You: {message}\n") { Foreground = Brushes.LightGreen };
            Paragraph para = new Paragraph(run);
            chatDisplay.Document.Blocks.Add(para);
            chatDisplay.ScrollToEnd();
        }

        private void AppendBotMessage(string message)
        {
            Run run = new Run($"CyberGuardian: {message}\n\n") { Foreground = Brushes.Cyan };
            Paragraph para = new Paragraph(run);
            chatDisplay.Document.Blocks.Add(para);
            chatDisplay.ScrollToEnd();
        }

        private void Speak(string text)
        {
            if (voiceEnabled && synthesizer != null && !string.IsNullOrWhiteSpace(text))
                try { synthesizer.SpeakAsync(text); } catch { }
        }

        private void VoiceToggleButton_Click(object sender, RoutedEventArgs e)
        {
            voiceEnabled = !voiceEnabled;
            voiceToggleButton.Content = voiceEnabled ? "🔊 Voice On" : "🔇 Voice Off";
            AppendBotMessage(voiceEnabled ? "Voice output enabled." : "Voice output disabled.");
            LogActivity($"Voice toggled: {voiceEnabled}", "System");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            LogActivity("Application closing", "System");
            synthesizer?.Dispose();
            base.OnClosing(e);
        }

        // ================== CHATBOT LOGIC (INNER) ==================
        private class ChatbotLogic
        {
            public string UserName { get; set; }
            public string FavoriteTopic { get; set; }
            public string LastTopic { get; set; }
            private int FollowUpCount { get; set; }
            private readonly Dictionary<string, List<string>> keywordResponses;
            private readonly Dictionary<string, string> sentimentReplies;
            private readonly List<string> defaultResponses;

            public ChatbotLogic()
            {
                UserName = null;
                FavoriteTopic = null;
                LastTopic = null;
                FollowUpCount = 0;

                keywordResponses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["password"] = new List<string> { "🔐 Use strong, unique passwords...", "🚫 Never share your password...", "✅ A good password is at least 12 characters..." },
                    ["scam"] = new List<string> { "⚠️ Scammers often create urgency...", "📞 If someone calls asking for money...", "🛡️ Report scams to SAFPS..." },
                    ["privacy"] = new List<string> { "👤 Adjust your social media privacy...", "🔒 Use encrypted messaging apps...", "📧 Be careful what personal data you share..." },
                    ["phishing"] = new List<string> { "🎣 Phishing emails often have spelling errors...", "🔗 Never click on suspicious links...", "📎 Don't open unexpected attachments..." },
                    ["general"] = new List<string> { "Cybersecurity is everyone's responsibility...", "Think before you click...", "Back up your important files..." }
                };

                sentimentReplies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["worried"] = "It's completely normal to feel worried. Let me give you a practical tip to ease your mind.",
                    ["frustrated"] = "I understand this can be frustrating. Take a deep breath – here's something helpful.",
                    ["curious"] = "Great curiosity! Learning about cybersecurity is the first step to staying safe."
                };

                defaultResponses = new List<string>
                {
                    "I'm not sure I understand. Can you try rephrasing?",
                    "Could you ask about a cybersecurity topic like passwords, scams, or privacy?",
                    "Hmm, I didn't catch that. Try asking for a tip about phishing or online safety."
                };
            }

            public string GetResponse(string userInput, out bool topicChanged)
            {
                topicChanged = false;
                if (string.IsNullOrWhiteSpace(userInput)) return "Please type something.";

                string lowerInput = userInput.ToLower().Trim();

                // Sentiment
                foreach (var sentiment in sentimentReplies)
                    if (lowerInput.Contains(sentiment.Key))
                        return $"{sentiment.Value}\n\n{GetRandomResponseForTopic("general")}";

                // Follow-up
                if (IsFollowUpRequest(lowerInput) && !string.IsNullOrEmpty(LastTopic))
                {
                    topicChanged = false;
                    FollowUpCount++;
                    return GetRandomResponseForTopic(LastTopic);
                }

                // Store name
                if (string.IsNullOrEmpty(UserName) && lowerInput.Contains("my name is"))
                {
                    int idx = lowerInput.IndexOf("my name is") + 10;
                    if (idx < lowerInput.Length) UserName = lowerInput.Substring(idx).Trim();
                }

                // Store favorite topic
                if (string.IsNullOrEmpty(FavoriteTopic))
                {
                    string[] topics = { "password", "scam", "privacy", "phishing" };
                    foreach (var t in topics)
                        if (lowerInput.Contains($"interested in {t}") || lowerInput.Contains($"like {t}"))
                            FavoriteTopic = t;
                }

                // Detect topic
                string detectedTopic = null;
                if (lowerInput.Contains("phish")) detectedTopic = "phishing";
                else if (lowerInput.Contains("password")) detectedTopic = "password";
                else if (lowerInput.Contains("scam") || lowerInput.Contains("fraud")) detectedTopic = "scam";
                else if (lowerInput.Contains("privacy") || lowerInput.Contains("personal data")) detectedTopic = "privacy";
                else if (lowerInput.Contains("general") || lowerInput.Contains("tip") || lowerInput.Contains("advice")) detectedTopic = "general";

                if (detectedTopic != null)
                {
                    if (detectedTopic != LastTopic) { LastTopic = detectedTopic; FollowUpCount = 0; topicChanged = true; }
                    else topicChanged = false;
                    string response = GetRandomResponseForTopic(detectedTopic);
                    if (!string.IsNullOrEmpty(FavoriteTopic) && detectedTopic.Equals(FavoriteTopic, StringComparison.OrdinalIgnoreCase))
                        response = $"As someone interested in {FavoriteTopic}, here's a tip: {response}";
                    return response;
                }

                LastTopic = null;
                return GetRandomDefaultResponse();
            }

            private bool IsFollowUpRequest(string input)
            {
                string[] phrases = { "tell me more", "another tip", "explain more", "continue", "go on" };
                return phrases.Any(p => input.Contains(p));
            }

            private string GetRandomResponseForTopic(string topic)
            {
                if (keywordResponses.TryGetValue(topic, out var responses))
                {
                    Random rnd = new Random();
                    return responses[rnd.Next(responses.Count)];
                }
                return GetRandomDefaultResponse();
            }

            private string GetRandomDefaultResponse()
            {
                Random rnd = new Random();
                return defaultResponses[rnd.Next(defaultResponses.Count)];
            }
        }

        // ================== QUIZ MANAGER ==================
        private class QuizManager
        {
            public List<QuizQuestion> Questions { get; set; }

            public QuizManager()
            {
                Questions = new List<QuizQuestion>
                {
                    new QuizQuestion { Question = "What is a common sign of a phishing email?",
                        Options = new List<string> { "Spelling errors", "Personalised greeting", "Company logo", "Professional signature" }, CorrectIndex = 0 },
                    new QuizQuestion { Question = "How long should a strong password ideally be?",
                        Options = new List<string> { "6 characters", "8 characters", "12+ characters", "Same as username" }, CorrectIndex = 2 },
                    new QuizQuestion { Question = "What does 2FA stand for?",
                        Options = new List<string> { "Two-Factor Authentication", "2-Factor Access", "Second-Factor Action", "Double-Factor Acknowledge" }, CorrectIndex = 0 },
                    new QuizQuestion { Question = "Which is the safest way to manage multiple passwords?",
                        Options = new List<string> { "Write them down", "Use a password manager", "Use the same password everywhere", "Save in email" }, CorrectIndex = 1 },
                    new QuizQuestion { Question = "What should you do if you receive a suspicious email?",
                        Options = new List<string> { "Reply and ask questions", "Click the links to check", "Forward to friends", "Report and delete" }, CorrectIndex = 3 },
                    new QuizQuestion { Question = "True or False: Using public Wi-Fi without a VPN is safe for banking.",
                        Options = new List<string> { "True", "False" }, CorrectIndex = 1 },
                    new QuizQuestion { Question = "What is social engineering?",
                        Options = new List<string> { "A type of software", "Manipulating people to reveal info", "A hacking tool", "Network security" }, CorrectIndex = 1 },
                    new QuizQuestion { Question = "How often should you update your software?",
                        Options = new List<string> { "Once a year", "When prompted", "Never", "Only if it's free" }, CorrectIndex = 1 },
                    new QuizQuestion { Question = "What is a VPN used for?",
                        Options = new List<string> { "Encrypt internet traffic", "Boost internet speed", "Block all ads", "Free storage" }, CorrectIndex = 0 },
                    new QuizQuestion { Question = "True or False: A strong password can be 'password123'.",
                        Options = new List<string> { "True", "False" }, CorrectIndex = 1 },
                    new QuizQuestion { Question = "Which of these is a secure way to authenticate?",
                        Options = new List<string> { "Biometrics", "SMS code", "Email link", "Security question" }, CorrectIndex = 0 },
                    new QuizQuestion { Question = "What should you do with old devices before disposal?",
                        Options = new List<string> { "Throw away", "Wipe data securely", "Give to a friend", "Recycle without wiping" }, CorrectIndex = 1 }
                };
            }
        }

        private class QuizQuestion
        {
            public string Question { get; set; }
            public List<string> Options { get; set; }
            public int CorrectIndex { get; set; }
        }

        // ================== TASK ITEM ==================
        public class TaskItem : INotifyPropertyChanged
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public DateTime? ReminderDate { get; set; }
            public string ReminderDisplay => ReminderDate.HasValue ? $"⏰ {ReminderDate.Value.ToShortDateString()}" : "";
            private bool isCompleted;
            public bool IsCompleted { get => isCompleted; set { isCompleted = value; OnPropertyChanged("IsCompleted"); } }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        }
    }





    
}

