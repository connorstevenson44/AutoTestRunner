using System.Windows;

namespace OrderDesk;

/// <summary>
/// Staff sign in before they can take orders. It fills in who signed in last, and a wrong password says so, empties the
/// password box and puts the keyboard back in it.
/// </summary>
public partial class SignInWindow : Window
{
    private readonly OrderStore _store;
    private readonly Connections _connections;

    public SignInWindow(OrderStore store, Connections connections)
    {
        InitializeComponent();
        (_store, _connections) = (store, connections);
        ShopText.Text = connections.ShopName;
        Loaded += (_, _) =>
        {
            if (Remembered.LastUser is { Length: > 0 } last)
            {
                UserBox.Text = last;
                PasswordBox.Focus();
            }
            else
                UserBox.Focus();
        };
    }

    private void OnChanged(object sender, RoutedEventArgs e) =>
        SignInButton.IsEnabled = UserBox.Text.Trim().Length > 0 && PasswordBox.Password.Length > 0;

    private void OnSignIn(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        if (_store.SignIn(user, PasswordBox.Password) is not { } name)
        {
            _store.Log($"Sign-in failed for {user}");
            SignInProblem.Text = "The user name or password is wrong.";
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }
        Remembered.LastUser = user;
        _store.Log($"Signed in: {user} ({name})");
        var main = new MainWindow(_store, _connections, name);
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();
}
