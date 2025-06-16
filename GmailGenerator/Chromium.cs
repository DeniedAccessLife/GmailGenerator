using System;
using System.IO;
using System.Net;
using System.Linq;
using OpenQA.Selenium;
using System.Threading;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Threading.Tasks;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GmailGenerator
{
    static class Chromium
    {
        public static string version;
        private static ChromeDriver driver;
        private static int currentProxyIndex = 0;
        private static List<string> proxies = new List<string>();

        public static void Initialization()
        {
            string temp_directory = Path.GetTempPath();
            string current_directory = Directory.GetCurrentDirectory();
            string chromium_directory = Path.Combine(current_directory, "Chromium");
            string driver_path = Path.Combine(current_directory, "chromedriver.exe");

            string driver_endpoint = "https://storage.googleapis.com/chrome-for-testing-public/";
            string github_endpoint = "https://api.github.com/repos/ungoogled-software/ungoogled-chromium-windows/releases";

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/85.0.4183.83 Safari/537.36");

                    if (!Directory.Exists(chromium_directory))
                    {
                        Console.WriteLine("Fetching the latest Chromium release from GitHub...");
                        string json = client.DownloadString(github_endpoint);

                        if (!string.IsNullOrEmpty(json))
                        {
                            JArray array = JArray.Parse(json);
                            JObject lasted = (JObject)array.FirstOrDefault();

                            if (lasted != null)
                            {
                                string tag = lasted["tag_name"].ToString();
                                version = Regex.Replace(tag, @"-\d+\.\d+", "");
                                Console.WriteLine($"Latest release: {tag} (driver version: {version})");

                                string file_scheme = Environment.Is64BitOperatingSystem ? "x64.zip" : "x86.zip";
                                IEnumerable<JToken> assets = lasted["assets"].Where(a => a["name"].ToString().Contains(file_scheme));

                                if (assets.Any())
                                {
                                    string chromium_url = assets.First()["browser_download_url"].ToString();
                                    string chromium_zip = Path.Combine(temp_directory, Path.GetFileName(chromium_url));

                                    Console.WriteLine($"Downloading: {Path.GetFileName(chromium_url)}");
                                    client.DownloadFile(chromium_url, chromium_zip);

                                    Console.WriteLine("Extracting Chromium...");
                                    ZipFile.ExtractToDirectory(chromium_zip, current_directory);

                                    string chromium_extracted = Directory.GetDirectories(current_directory).FirstOrDefault(d => d.Contains("ungoogled-chromium"));
                                    if (!string.IsNullOrEmpty(chromium_extracted))
                                    {
                                        Directory.Move(chromium_extracted, chromium_directory);
                                        Console.WriteLine($"Chromium extracted and renamed to: {chromium_directory}");
                                    }

                                    File.Delete(chromium_zip);
                                }
                                else
                                {
                                    Console.WriteLine("No valid file found for download!");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Failed to find the latest release!");
                            }
                        }
                    }
                    else
                    {
                        version = GetChromiumVersion();

                        if (string.IsNullOrEmpty(version))
                        {
                            Console.WriteLine("Could not determine Chromium version. Aborting driver download.");
                            return;
                        }

                        Console.WriteLine("Chromium is already installed. Skipping download.");
                    }

                    if (!File.Exists(driver_path))
                    {
                        if (string.IsNullOrEmpty(version))
                        {
                            Console.WriteLine("Version is not set. Cannot download driver.");
                            return;
                        }

                        string driver_url = $"{driver_endpoint}{version}/win64/chromedriver-win64.zip";
                        string driver_zip = Path.Combine(temp_directory, $"chromedriver_{version}.zip");

                        Console.WriteLine($"Downloading driver for version {version}");
                        client.DownloadFile(driver_url, driver_zip);

                        string driver_extracted = Path.Combine(temp_directory, "chromedriver-temp");
                        Console.WriteLine("Extracting driver...");
                        ZipFile.ExtractToDirectory(driver_zip, driver_extracted);

                        string downloaded_driver_path = Directory.GetFiles(driver_extracted, "chromedriver.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (File.Exists(downloaded_driver_path))
                        {
                            File.Move(downloaded_driver_path, driver_path);
                            Console.WriteLine($"Driver extracted to: {driver_path}");
                        }

                        File.Delete(driver_zip);
                        Directory.Delete(driver_extracted, true);
                    }
                    else
                    {
                        Console.WriteLine("Driver is already installed. Skipping download.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred: {ex.Message}");
            }
        }

        private static void LoadProxies()
        {
            if (File.Exists("proxies.txt"))
            {
                proxies = File.ReadAllLines("proxies.txt").ToList();
            }
        }

        private static string GetNextProxy()
        {
            if (proxies.Count == 0)
            {
                LoadProxies();
            }

            if (proxies.Count == 0)
            {
                throw new Exception("No proxies available!");
            }

            string proxy = proxies[currentProxyIndex];
            Console.WriteLine($"Using proxy #{currentProxyIndex + 1}: {proxy}");
            proxies.RemoveAt(currentProxyIndex);
            File.WriteAllLines("proxies.txt", proxies);

            if (currentProxyIndex >= proxies.Count)
            {
                currentProxyIndex = 0;
            }

            return proxy;
        }

        public static Task Start()
        {
            Random random = new Random();
            Console.Write("Enter the number of Gmail accounts to generate: ");
            int count = int.Parse(Console.ReadLine());

            LoadProxies();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    string proxy = GetNextProxy();

                    driver = new ChromeDriver(GetChromeDriverService(), GetChromeOptions(proxy), TimeSpan.FromMinutes(2));

                    // Test proxy connection
                    Console.WriteLine("Testing proxy connection...");
                    try
                    {
                        driver.Navigate().GoToUrl("https://api.ipify.org?format=json");
                        Thread.Sleep(5000);
                        string pageSource = driver.PageSource;

                        // Extract IP from JSON response
                        var match = Regex.Match(pageSource, @"""ip"":""([^""]+)""");
                        if (match.Success)
                        {
                            Console.WriteLine($"Current IP: {match.Groups[1].Value}");
                        }
                        else
                        {
                            Console.WriteLine("Could not extract IP from response");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Proxy test failed: {ex.Message}");
                        if (driver != null)
                        {
                            driver.Quit();
                        }
                        continue;
                    }

                    var (firstName, lastName) = Utils.GenerateRandomName();
                    string password = Utils.Random(16);
                    var (year, month, day) = Utils.GenerateBirthday();
                    string username = $"{firstName.ToLower()}.{lastName.ToLower()}{new Random().Next(10000)}";

                    driver.Navigate().GoToUrl("https://accounts.google.com/signup");
                    Thread.Sleep(2000);

                    // Fill first name
                    Input(driver, By.Name("firstName"), firstName);
                    Thread.Sleep(500);

                    // Fill last name
                    Input(driver, By.Name("lastName"), lastName);
                    Thread.Sleep(500);

                    // Click Next
                    Click(driver, By.CssSelector("button[jsname='LgbsSe']"));
                    Thread.Sleep(2000);

                    // Fill birthday
                    Input(driver, By.Name("day"), day.ToString());
                    Thread.Sleep(500);

                    // Select month
                    Click(driver, By.CssSelector("div[id='month'] div[jsname='oYxtQd']"));
                    Thread.Sleep(1000);
                    Click(driver, By.CssSelector($"li[data-value='{month}']"));
                    Thread.Sleep(500);

                    // Fill year
                    Input(driver, By.Name("year"), year.ToString());
                    Thread.Sleep(3000);

                    // Select gender based on first name
                    try
                    {
                        // Find gender field wrapper and click the dropdown
                        var genderFieldWrapper = driver.FindElement(By.CssSelector("div[id='gender']"));
                        var genderField = genderFieldWrapper.FindElement(By.CssSelector("div[jsname='oYxtQd']"));
                        genderField.Click();
                        Thread.Sleep(1000);

                        // Find dropdown and select option
                        var dropdown = genderFieldWrapper.FindElement(By.CssSelector("ul[jsname='rymPhb']"));
                        if (dropdown != null)
                        {
                            // Select gender based on first name (1 for male, 2 for female)
                            int genderValue = firstName.ToLower().StartsWith("j") || firstName.ToLower().StartsWith("m") ||
                                            firstName.ToLower().StartsWith("r") || firstName.ToLower().StartsWith("w") ||
                                            firstName.ToLower().StartsWith("d") || firstName.ToLower().StartsWith("t") ||
                                            firstName.ToLower().StartsWith("c") || firstName.ToLower().StartsWith("k") ||
                                            firstName.ToLower().StartsWith("g") || firstName.ToLower().StartsWith("e") ? 1 : 2;

                            var options = dropdown.FindElements(By.CssSelector("li[data-value]"));

                            foreach (var option in options)
                            {
                                if (option.GetAttribute("data-value") == genderValue.ToString())
                                {
                                    // Scroll option into view
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", option);
                                    Thread.Sleep(300);
                                    option.Click();
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Gender dropdown not found");
                        }

                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error selecting gender: {ex.Message}");
                    }

                    // Click Next
                    Click(driver, By.CssSelector("button[type='button']"));
                    Thread.Sleep(3000);

                    // Fill username
                    Input(driver, By.Name("Username"), username);
                    Thread.Sleep(3000);

                    // Click Next
                    Click(driver, By.CssSelector("button[jsname='LgbsSe']"));
                    Thread.Sleep(2000);

                    // Fill password
                    Input(driver, By.Name("Passwd"), password);
                    Thread.Sleep(3000);

                    // Confirm password
                    Input(driver, By.Name("PasswdAgain"), password);
                    Thread.Sleep(3000);

                    // Click Next
                    Click(driver, By.CssSelector("button[jsname='LgbsSe']"));
                    Thread.Sleep(3000);

                    Console.WriteLine($"{username}@gmail.com:{password}");
                    File.AppendAllText("GmailAccounts.txt", $"{username}@gmail.com:{password}\n");
                    Thread.Sleep(3000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");

                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                    }
                }
                finally
                {
                    // driver.Quit();
                }
            }

            return Task.CompletedTask;
        }

        private static string GetChromiumVersion()
        {
            string chromium_directory = Path.Combine(Directory.GetCurrentDirectory(), "Chromium");
            string manifest_file = Directory.GetFiles(chromium_directory, "*.manifest").FirstOrDefault();

            if (!string.IsNullOrEmpty(manifest_file))
            {
                string version = Path.GetFileNameWithoutExtension(manifest_file);
                Console.WriteLine($"Chromium version from manifest: {version}");
                return version;
            }

            string chromium_file = Path.Combine(chromium_directory, "chrome.exe");

            if (File.Exists(chromium_file))
            {
                try
                {
                    string version = FileVersionInfo.GetVersionInfo(chromium_file).FileVersion;
                    Console.WriteLine($"Chromium version from chrome.exe: {version}");
                    return version;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to get version from chrome.exe: {ex.Message}");
                }
            }

            Console.WriteLine("Version not found!");
            return null;
        }

        private static ChromeDriverService GetChromeDriverService()
        {
            ChromeDriverService service = ChromeDriverService.CreateDefaultService();
            service.EnableVerboseLogging = false;
            service.SuppressInitialDiagnosticInformation = true;
            return service;
        }

        private static ChromeOptions GetChromeOptions(string proxy)
        {
            ChromeOptions options = new ChromeOptions();
            options.BinaryLocation = Path.Combine(Directory.GetCurrentDirectory(), "Chromium", "chrome.exe");

            options.AddArgument("--start-maximized");
            options.AddExcludedArgument("--enable-automation");
            options.AddUserProfilePreference("credentials_enable_service", false);
            options.AddUserProfilePreference("profile.default_content_setting_values.images", 2);

            if (!string.IsNullOrEmpty(proxy))
            {
                try
                {
                    options.AddArgument($"--proxy-server=http://{proxy}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error configuring proxy: {ex.Message}");
                }
            }

            return options;
        }

        public static void Click(ChromeDriver driver, By by)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(40));

            bool element = wait.Until(condition =>
            {
                try
                {
                    IWebElement e = driver.FindElement(by);

                    if (e != null && e.Displayed)
                    {
                        e.Click();
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (StaleElementReferenceException)
                {
                    return false;
                }
                catch (NoSuchElementException)
                {
                    return false;
                }
            });
        }

        public static void Input(ChromeDriver driver, By by, string text)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

            bool element = wait.Until(condition =>
            {
                try
                {
                    IWebElement e = driver.FindElements(by).FirstOrDefault();

                    if (e != null && e.Displayed)
                    {
                        e.SendKeys(text);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (StaleElementReferenceException)
                {
                    return false;
                }
                catch (NoSuchElementException)
                {
                    return false;
                }
            });
        }
    }
}