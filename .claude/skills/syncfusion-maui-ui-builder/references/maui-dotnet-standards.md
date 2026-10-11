# MAUI & .NET Standards & Compliance Reference

**Version:** 1.0.0  
**Last Updated:** May 5, 2026  
**Purpose:** WCAG 2.2 AA (mobile accessibility), .NET security, MAUI performance, and C# code quality standards enforced during Stage 6 validation

---

## Table of Contents

1. [Accessibility Standards (WCAG 2.2 AA for Mobile)](#accessibility-standards-wcag-22-aa-for-mobile)
2. [Security Standards (.NET)](#security-standards-net)
3. [Performance Standards (MAUI)](#performance-standards-maui)
4. [Code Quality Standards (C#)](#code-quality-standards-c)
5. [Cross-Platform Standards (iOS, Android, Windows, macOS)](#cross-platform-standards)
6. [Validation Checklist](#validation-checklist)
7. [Auto-Fix Rules](#auto-fix-rules)

---

## Accessibility Standards (WCAG 2.2 AA for Mobile)

### WCAG Principles: POUR (Adapted for MAUI)

| Principle | Description | MAUI Implementation |
|-----------|-------------|-------------------|
| **P**erceivable | Content can be perceived through different senses (text alternatives, contrast, clear structure) | AutomationId on all controls, proper contrast in ResourceDictionary, semantic XAML |
| **O**perable | Interface can be operated by all users (keyboard, no traps, sufficient target size) | Keyboard navigation via Tab, proper focus handling, 44x44 dp touch targets |
| **U**nderstandable | Content and interface are understandable (clear language, predictable, error guidance) | Clear error messages, logical navigation flow, platform-consistent patterns |
| **R**obust | Content works with assistive technologies (semantic controls, proper properties, screen reader compatible) | Syncfusion controls with accessibility properties, SemanticDescription, platform defaults |

---

### Perceivable: Content Must Be Perceivable

#### 1.1 Text Alternatives (Images & Icons)

**Images require descriptive text via SemanticDescription:**

```xaml
<!-- ❌ Missing SemanticDescription -->
<Image Source="chart.png" />

<!-- ✅ Descriptive SemanticDescription for informative image -->
<Image Source="chart.png" 
       SemanticProperties.Description="Bar chart showing 40% increase in Q3 sales" />

<!-- ✅ Decorative image (empty description) -->
<Image Source="decorative-border.png" 
       SemanticProperties.Description="" />

<!-- ✅ Complex image with detailed description -->
<VerticalStackLayout>
  <Image Source="infographic.png" 
         AutomationId="marketTrendsInfographic"
         SemanticProperties.Description="2024 market trends infographic">
    <Image.GestureRecognizers>
      <TapGestureRecognizer Command="{Binding ShowDescriptionCommand}" />
    </Image.GestureRecognizers>
  </Image>
  <Label x:Name="InfographicDescription" 
         Text="Detailed 2024 market trends description..." />
</VerticalStackLayout>
```

**Icon buttons need accessible names via AutomationId:**

```xaml
<!-- ❌ No accessible name -->
<ImageButton Source="menu_icon.png" />

<!-- ✅ Using AutomationId and ToolTipProperties -->
<ImageButton Source="menu_icon.png"
             AutomationId="openMenuButton"
             ToolTipProperties.Text="Open menu"
             Clicked="OnMenuClicked" />

<!-- ✅ Using Syncfusion button with text -->
<SfButton Text="☰" 
          AutomationId="openMenuButton"
          Clicked="OnMenuClicked" />

<!-- ✅ Using semantic button -->
<Button AutomationId="openMenuButton"
        SemanticProperties.Hint="Navigation menu"
        Clicked="OnMenuClicked">
  <Label Text="☰" FontSize="20" />
</Button>
```

**Validation Rules:**
- [ ] All informative images have descriptive SemanticProperties.Description
- [ ] Decorative images have empty SemanticProperties.Description
- [ ] Icon-only buttons have AutomationId and ToolTipProperties.Text
- [ ] Complex images have SemanticProperties.Description with detailed text or tap handler for details
- [ ] Images are not the only way to convey information

---

#### 1.2 Media Alternatives (Video & Audio)

```xaml
<!-- Video with captions (using MediaElement or third-party player) -->
<VerticalStackLayout>
  <Label Text="Product Demo" AutomationId="videoDemoTitle" />
  <!-- Use Syncfusion MediaPlayer or custom MediaElement with caption support -->
  <WebView Source="https://player.vimeo.com/video/123456?title=0&byline=0&portrait=0" />
  <Label Text="Captions available in settings"
         SemanticProperties.Description="Video includes captions for hearing impaired users" />
</VerticalStackLayout>

<!-- Audio with transcript -->
<VerticalStackLayout>
  <Label Text="Podcast Episode 42" AutomationId="podcastTitle" />
  <MediaElement Source="podcast.mp3" ShowsPlaybackControls="True" />
  <Expander>
    <Expander.Header>
      <Label Text="Show Transcript" />
    </Expander.Header>
    <Label Text="Full transcript text of the podcast episode..." 
           LineBreakMode="WordWrap" />
  </Expander>
</VerticalStackLayout>
```

**Validation Rules:**
- [ ] Videos have captions or transcript
- [ ] Videos have audio descriptions (audio track or transcript)
- [ ] Media players have play/pause controls
- [ ] Auto-playing media can be stopped
- [ ] Transcript provided for audio content

---

#### 1.4 Color Contrast

**Minimum Ratios (WCAG 2.2 AA for Mobile):**

| Text Type | Minimum Ratio | MAUI FontSize |
|-----------|---------------|--------------|
| Normal text (small) | 4.5:1 | < 18 |
| Large text | 3:1 | ≥ 18 |
| UI components & graphics | 3:1 | Any |
| Focus indicators | 3:1 | Any |

```xaml
<!-- ✓ GOOD - High contrast in ResourceDictionary -->
<ResourceDictionary>
  <Color x:Key="TextOnLight">#000000</Color>
  <Color x:Key="SurfaceLight">#ffffff</Color>
  <!-- Contrast ratio: 21:1 ✓ -->
  
  <!-- ✗ BAD - Low contrast (fails) -->
  <!-- <Color x:Key="HintText">#cccccc</Color> - Only 1.9:1, FAILS -->
  
  <!-- ✓ GOOD - Adequate contrast for hints -->
  <Color x:Key="HintText">#666666</Color>
  <!-- Contrast: 5.8:1 ✓ -->
</ResourceDictionary>

<!-- Using contrasting colors -->
<Label Text="Welcome" 
       TextColor="{StaticResource TextOnLight}"
       BackgroundColor="{StaticResource SurfaceLight}" />

<!-- ✓ GOOD - Focus indicator with sufficient contrast -->
<Frame BorderColor="#0066cc" HasShadow="True">
  <Label Text="Focused element" BackgroundColor="#0066cc" TextColor="#ffffff" />
</Frame>
<!-- Contrast: 8.6:1 ✓ -->
```

**Don't rely on color alone to convey information:**

```xaml
<!-- ❌ Only color indicates error (not accessible) -->
<SfTextEntry BorderColor="Red" 
             Text="{Binding Email}" />

<!-- ✅ Color + icon + text (accessible) -->
<VerticalStackLayout>
  <SfTextEntry x:Name="EmailEntry"
               Text="{Binding Email}"
               BorderColor="{Binding IsEmailValid, Converter={StaticResource BoolToColorConverter}}"
               AutomationId="emailInput"
               SemanticProperties.Hint="Email address" />
  <StackLayout IsVisible="{Binding ShowEmailError, Converter={StaticResource InvertedBoolConverter}}">
    <Label Text="❌ Please enter a valid email address"
           TextColor="Red"
           AutomationId="emailError"
           SemanticProperties.Description="Validation error: invalid email format" />
  </StackLayout>
</VerticalStackLayout>
```

**Validation Rules:**
- [ ] All text has contrast ≥ 4.5:1 (normal) or 3:1 (large)
- [ ] Focus indicators have contrast ≥ 3:1
- [ ] Placeholder text has minimum 4.5:1 contrast
- [ ] Icons conveying information have 3:1 contrast
- [ ] Information not conveyed by color alone
- [ ] AppTheme (dark/light mode) maintains contrast in both modes

---

### Operable: Users Must Be Able to Operate the Interface

#### 2.1 Keyboard Navigation (Mobile-Optimized)

**All functionality must be accessible via keyboard and touch—no touch-only actions:**

```csharp
// ❌ BAD - Only handles Clicked event (touch/mouse-only)
myButton.Clicked += (s, e) => HandleAction();

// ✅ GOOD - Handles Clicked event and keyboard via AutomationId
public partial class MyPage : ContentPage
{
    public MyPage()
    {
        InitializeComponent();
        
        // Keyboard support via platform integration
        myButton.Clicked += (s, e) => HandleAction();
        myButton.AutomationId = "actionButton";
    }
    
    private void HandleAction()
    {
        // Action implementation
    }
}
```

**XAML bindings for proper keyboard/touch support:**

```xaml
<!-- ❌ BAD - Only click gesture (touch-only) -->
<Label Text="Action">
  <Label.GestureRecognizers>
    <TapGestureRecognizer Command="{Binding ActionCommand}" />
  </Label.GestureRecognizers>
</Label>

<!-- ✅ GOOD - Button with keyboard support built-in -->
<Button Text="Action"
        Command="{Binding ActionCommand}"
        AutomationId="actionButton" />

<!-- ✅ GOOD - Escape closes modal, Tab navigates -->
<ContentPage x:Name="ModalPage"
             Loaded="OnModalLoaded">
  <!-- Content -->
  <Button Text="Close"
          Command="{Binding CloseCommand}"
          AutomationId="closeButton" />
</ContentPage>
```

**No keyboard traps—users must be able to navigate out of every control:**

```csharp
// Code-behind for modal navigation
public partial class ModalPage : ContentPage
{
    public ModalPage()
    {
        InitializeComponent();
        this.Loaded += OnPageLoaded;
    }
    
    private void OnPageLoaded(object sender, EventArgs e)
    {
        // Ensure first interactive element gets focus
        firstButton?.Focus();
    }
}
```

**Validation Rules:**
- [ ] All interactive controls are tabbable (Tab key navigates through them)
- [ ] Tab order is logical (top-to-bottom, left-to-right)
- [ ] Can navigate into and out of every control
- [ ] Escape key closes modals and popups
- [ ] Enter/Space activates buttons
- [ ] Arrow keys work for CollectionView, TabView, picker selections
- [ ] No focus traps in any control

---

#### 2.4 Focus Management (MAUI Controls)

**MAUI controls have built-in focus support—ensure visibility:**

```xaml
<!-- ✅ GOOD - SfButton has automatic focus visibility -->
<SfButton Text="Submit"
          AutomationId="submitButton"
          Clicked="OnSubmitClicked" />

<!-- ✅ GOOD - SfTextEntry focus styling -->
<SfTextEntry x:Name="EmailEntry"
             Placeholder="Enter email"
             AutomationId="emailInput"
             PlaceholderColor="#999999"
             StrokeColor="{AppThemeBinding Light=#0066cc, Dark=#4D94FF}" />

<!-- ✅ GOOD - Focus not obscured by AppBar -->
<Grid RowDefinitions="Auto,*" PaddingTop="{OnPlatform iOS=40}">
  <Shell.TitleView>
    <Grid ColumnDefinitions="*, Auto" Padding="16,0" ColumnSpacing="10">
      <Label Text="Page Title" FontSize="18" FontAttributes="Bold" />
    </Grid>
  </Shell.TitleView>
  
  <ScrollView Grid.Row="1" Padding="16">
    <!-- Content with proper scroll-margin equivalent -->
    <StackLayout Spacing="10">
      <SfTextEntry x:Name="FirstInput"
                   AutomationId="firstInput"
                   Margin="0,16" />
      <!-- Focus won't be obscured by header -->
    </StackLayout>
  </ScrollView>
</Grid>
```

**Manage focus when opening modals or popups:**

```csharp
public partial class ModalPage : ContentPage
{
    public ModalPage()
    {
        InitializeComponent();
        this.Loaded += OnModalLoaded;
    }

    private void OnModalLoaded(object sender, EventArgs e)
    {
        // Focus first interactive element when modal opens
        firstButton?.Focus();
    }
    
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Close modal on Escape
        if (e.Key == KeyCode.Escape)
        {
            this.Close();
            e.Handled = true;
        }
    }
}
```

**Validation Rules:**
- [ ] Focus indicators always visible in MAUI controls
- [ ] Focus outline or highlight has ≥ 3:1 contrast
- [ ] Focus order logical (top-to-bottom, left-to-right)
- [ ] Focus managed when modal/popup opens (first control focused)
- [ ] Focused element not obscured by AppBar or sticky content
- [ ] Focus visible on all platforms (iOS, Android, Windows, macOS)

---

#### 2.5 Target Size (Mobile Touch Targets)

**Interactive targets must be at least 24 × 24 device-independent pixels (dp), recommended 44 × 44 dp for touch:**

```xaml
<!-- ✓ GOOD - Minimum target size (24×24 dp) -->
<SfButton Text="OK"
          Padding="12"
          FontSize="14"
          MinimumWidthRequest="44"
          MinimumHeightRequest="44" />

<!-- ✓ GOOD - Comfortable target size for touch (44×44 dp recommended) -->
<Button Text="Submit"
        Padding="16,12"
        FontSize="16"
        MinimumWidthRequest="44"
        MinimumHeightRequest="44" />

<!-- ✓ GOOD - Icon button with adequate size -->
<ImageButton Source="menu_icon.png"
             Padding="12"
             MinimumWidthRequest="48"
             MinimumHeightRequest="48"
             AutomationId="menuButton" />

<!-- ✓ GOOD - Adequate spacing to prevent accidental activation -->
<StackLayout Spacing="16">
  <Button Text="Save" MinimumHeightRequest="44" />
  <Button Text="Cancel" MinimumHeightRequest="44" />
  <!-- 16 dp spacing between buttons prevents accidental taps -->
</StackLayout>

<!-- ✓ GOOD - Checkbox with adequate size -->
<CheckBox Scale="1.5"
          MinimumWidthRequest="44"
          MinimumHeightRequest="44"
          AutomationId="agreeCheckbox" />
```

**Exceptions:** Inline text links (if surrounded by adequate spacing), native browser controls, targets in specific layouts where proper spacing is maintained.

**Validation Rules:**
- [ ] All buttons ≥ 44×44 dp (minimum 24×24 dp, but 44×44 recommended for touch)
- [ ] All interactive controls ≥ 44×44 dp
- [ ] All form inputs ≥ 44×44 dp (including SfTextEntry, SfComboBox)
- [ ] Adequate spacing (≥ 8 dp) between touch targets to prevent accidental activation
- [ ] Target size consistent across all platforms (iOS, Android, Windows, macOS)

---

#### 2.6 Dragging & Gestures (Alternative Interactions)

**Any action triggered by dragging/gestures must offer button/menu alternatives:**

```xaml
<!-- ❌ Drag-only reorder (fails accessibility) -->
<CollectionView ItemsSource="{Binding Items}"
                x:Name="DragDropList">
  <CollectionView.ItemTemplate>
    <DataTemplate>
      <Label Text="{Binding Name}" />
    </DataTemplate>
  </CollectionView.ItemTemplate>
</CollectionView>

<!-- ✅ Drag + button alternatives for reordering -->
<VerticalStackLayout>
  <CollectionView ItemsSource="{Binding Items}"
                  x:Name="ItemsList">
    <CollectionView.ItemTemplate>
      <DataTemplate>
        <HorizontalStackLayout Spacing="8" Padding="8">
          <Label Text="{Binding Name}" />
          <Button Text="↑" 
                  Command="{Binding Source={RelativeSource AncestorType={x:Type local:ItemsPage}}, Path=BindingContext.MoveUpCommand}"
                  CommandParameter="{Binding .}"
                  MinimumWidthRequest="44"
                  MinimumHeightRequest="44"
                  AutomationId="{Binding Name, StringFormat='moveUp{0}'}" />
          <Button Text="↓"
                  Command="{Binding Source={RelativeSource AncestorType={x:Type local:ItemsPage}}, Path=BindingContext.MoveDownCommand}"
                  CommandParameter="{Binding .}"
                  MinimumWidthRequest="44"
                  MinimumHeightRequest="44"
                  AutomationId="{Binding Name, StringFormat='moveDown{0}'}" />
        </HorizontalStackLayout>
      </DataTemplate>
    </CollectionView.ItemTemplate>
  </CollectionView>
</VerticalStackLayout>

<!-- ✅ Swipe + button menu for actions -->
<CollectionView ItemsSource="{Binding Items}">
  <CollectionView.ItemTemplate>
    <DataTemplate>
      <SwipeView>
        <SwipeView.LeftItems>
          <SwipeItems>
            <SwipeItem Text="Edit"
                       BackgroundColor="Blue"
                       Command="{Binding Source={RelativeSource AncestorType={x:Type local:ItemsPage}}, Path=BindingContext.EditCommand}"
                       CommandParameter="{Binding .}" />
            <SwipeItem Text="Delete"
                       BackgroundColor="Red"
                       Command="{Binding Source={RelativeSource AncestorType={x:Type local:ItemsPage}}, Path=BindingContext.DeleteCommand}"
                       CommandParameter="{Binding .}" />
          </SwipeItems>
        </SwipeView.LeftItems>
        <Label Text="{Binding Name}" Padding="16" />
      </SwipeView>
    </DataTemplate>
  </CollectionView.ItemTemplate>
</CollectionView>
```

**Applies to:** 
- Sliders: Provide numeric input field alternative
- Swipe gestures: Provide menu/buttons alternative
- Pinch zoom: Provide plus/minus buttons
- Drawing/handwriting: Provide keyboard text input
- Image cropping: Provide preset buttons or numeric size entry

**Validation Rules:**
- [ ] All drag-based interactions have button/menu alternatives
- [ ] All swipe gestures have menu/context button alternatives
- [ ] All complex gestures have accessibility alternatives
- [ ] Alternatives are equally prominent and accessible

---

### Understandable: Content Must Be Understandable

#### 3.1 Language & Structure

```xaml
<!-- ✅ Page language specified in MauiProgram.cs -->
<!-- MauiProgram.cs -->
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

<!-- ✅ Proper semantic structure with AutomationId -->
<VerticalStackLayout Spacing="16" Padding="16">
  <Label Text="Product Details"
         FontSize="24"
         FontAttributes="Bold"
         AutomationId="pageTitle"
         SemanticProperties.HeadingLevel="1" />
  
  <Label Text="Description"
         FontSize="18"
         FontAttributes="Bold"
         AutomationId="descriptionHeading"
         SemanticProperties.HeadingLevel="2" />
  <Label Text="Product description text..." />
  
  <Label Text="Specifications"
         FontSize="18"
         FontAttributes="Bold"
         AutomationId="specsHeading"
         SemanticProperties.HeadingLevel="2" />
  <Label Text="Specification details..." />
</VerticalStackLayout>

<!-- ❌ BAD: Incorrect semantic structure -->
<VerticalStackLayout>
  <Label Text="Product Details" FontSize="24" />
  <!-- Skipped h2 level -->
  <Label Text="Specifications" FontSize="16" />  <!-- Too small, semantic level inconsistent -->
</VerticalStackLayout>
```

**Localization support:**

```csharp
// Supporting multiple languages in MAUI
public class AppResources
{
    public AppResources()
    {
        // Load translated strings based on culture
        _resourceManager = new ResourceManager(
            typeof(AppResource));
    }
    
    public string GetString(string key)
    {
        return _resourceManager.GetString(key);
    }
}
```

**Validation Rules:**
- [ ] Default language set in MauiProgram.cs
- [ ] Language/locale properly configured
- [ ] Semantic structure with proper heading levels (via SemanticProperties.HeadingLevel)
- [ ] Headings are descriptive
- [ ] Content organized logically with visual hierarchy

---

#### 3.2 Predictable Behavior

**Users must be able to predict what happens when they interact:**

```xaml
<!-- ✓ GOOD - Buttons trigger predictable actions -->
<Button Text="Save"
        Command="{Binding SaveCommand}"
        AutomationId="saveButton" />
<Button Text="Cancel"
        Command="{Binding CancelCommand}"
        AutomationId="cancelButton" />

<!-- ❌ BAD - Unexpected behavior -->
<Label Text="Click to save"
       AutomationId="saveText">
  <Label.GestureRecognizers>
    <TapGestureRecognizer Command="{Binding NavigateCommand}" />
  </Label.GestureRecognizers>
</Label>
<!-- Confusing: looks like text but navigates -->

<!-- ✓ GOOD - Focus doesn't trigger unwanted changes -->
<SfTextEntry x:Name="EmailField"
             Text="{Binding Email}"
             Focused="OnEmailFocused"
             AutomationId="emailInput" />

<!-- ❌ BAD - Focus triggers unexpected actions -->
<!-- Don't submit form on field focus -->
```

**Consistent navigation and help—features in same location across app:**

```xaml
<!-- Help consistently placed on all pages -->
<Shell>
  <TabBar>
    <ShellContent Title="Home" 
                  Icon="home.png"
                  ContentTemplate="{DataTemplate local:HomePage}" />
    <ShellContent Title="Settings" 
                  Icon="settings.png"
                  ContentTemplate="{DataTemplate local:SettingsPage}" />
  </TabBar>
  
  <!-- Consistent help menu -->
  <Shell.FlyoutHeader>
    <StackLayout Padding="16">
      <Label Text="Help & Support" FontAttributes="Bold" />
    </StackLayout>
  </Shell.FlyoutHeader>
  
  <Shell.FlyoutItems>
    <!-- Help items consistently placed -->
    <ShellContent Title="Contact Us" Route="contact" />
    <ShellContent Title="FAQs" Route="faqs" />
    <ShellContent Title="Help" Route="help" />
  </Shell.FlyoutItems>
</Shell>
```

**Validation Rules:**
- [ ] Buttons perform expected actions (Save saves, Cancel cancels)
- [ ] Navigation changes don't happen on focus
- [ ] Form field changes don't auto-submit
- [ ] Help and support are consistently placed
- [ ] Error messages appear near the field with the error

---

#### 3.3 Forms & Error Handling

**Every input needs an associated label:**

```xaml
<!-- ❌ No accessible label -->
<SfTextEntry Placeholder="Email" />

<!-- ✅ Label with AutomationId association -->
<VerticalStackLayout Spacing="4">
  <Label Text="Email address"
         AutomationId="emailLabel" />
  <SfTextEntry x:Name="EmailEntry"
               Placeholder="name@example.com"
               AutomationId="emailInput"
               SemanticProperties.Description="Enter your email address"
               Keyboard="Email" />
</VerticalStackLayout>

<!-- ✅ With instruction text -->
<VerticalStackLayout Spacing="4">
  <Label Text="Password"
         AutomationId="passwordLabel" />
  <SfTextEntry x:Name="PasswordEntry"
               IsPassword="True"
               Placeholder="Enter password"
               AutomationId="passwordInput"
               SemanticProperties.Description="Password requirements: at least 8 characters with one number" />
  <Label Text="At least 8 characters with one number"
         FontSize="12"
         TextColor="#666666"
         AutomationId="passwordRequirements" />
</VerticalStackLayout>
```

**Error messages must be clear and linked to fields:**

```xaml
<!-- ❌ Error unclear -->
<Label Text="Error" TextColor="Red" />
<SfTextEntry Text="{Binding Email}" />

<!-- ✅ Clear error with AutomationId linkage -->
<VerticalStackLayout Spacing="4">
  <Label Text="Email address"
         AutomationId="emailLabel" />
  <SfTextEntry x:Name="EmailEntry"
               Text="{Binding Email}"
               AutomationId="emailInput"
               IsVisible="{Binding IsEmailValid, Converter={StaticResource InvertedBoolConverter}}" />
  
  <StackLayout IsVisible="{Binding ShowEmailError}">
    <Label Text="❌ Invalid email format"
           TextColor="Red"
           FontAttributes="Bold"
           AutomationId="emailError"
           SemanticProperties.Description="Validation error: Please enter a valid email address (example: name@domain.com)" />
  </StackLayout>
</VerticalStackLayout>
```

**Don't force users to re-enter information:**

```csharp
// ViewModel - Auto-fill shipping from billing
public class CheckoutViewModel : INotifyPropertyChanged
{
    private Address _billingAddress;
    private Address _shippingAddress;
    private bool _useSameAddress = true;

    public bool UseSameAddress
    {
        get => _useSameAddress;
        set
        {
            if (_useSameAddress != value)
            {
                _useSameAddress = value;
                if (value)
                {
                    // Auto-populate shipping from billing
                    ShippingAddress = new Address
                    {
                        Street = _billingAddress.Street,
                        City = _billingAddress.City,
                        State = _billingAddress.State,
                        ZipCode = _billingAddress.ZipCode
                    };
                }
                OnPropertyChanged();
            }
        }
    }
}
```

**Authentication must not rely solely on cognitive tests:**

```xaml
<!-- ❌ Cognitive test only (puzzle, remember pattern) -->
<Button Text="Solve puzzle to login" />

<!-- ✅ Multiple authentication options -->
<StackLayout Spacing="16">
  <Button Text="Email me a login link"
          Command="{Binding SendLoginLinkCommand}"
          AutomationId="emailLoginButton" />
  <Button Text="Sign in with biometric"
          Command="{Binding BiometricLoginCommand}"
          AutomationId="biometricLoginButton" />
  <Button Text="Traditional username/password"
          Command="{Binding TraditionalLoginCommand}"
          AutomationId="usernameLoginButton" />
</StackLayout>
```

**Validation Rules:**
- [ ] Every input has associated Label
- [ ] Required fields marked with * or "(required)" text
- [ ] Error messages clear and specific
- [ ] Error messages linked via AutomationId/SemanticProperties
- [ ] Error messages include how to fix the problem
- [ ] Validation happens at appropriate times (field blur, form submit)
- [ ] Information not re-requested (use saved data when available)
- [ ] Authentication offers multiple methods (not purely cognitive test)

---

### Robust: Content Must Work with Assistive Technologies

#### 4.1 Semantic MAUI Controls

**Prefer native MAUI controls—they have accessibility built in:**

```xaml
<!-- ❌ Non-semantic custom control (harder to maintain) -->
<Frame BorderColor="Blue" Padding="12"
       GestureRecognizers="{new TapGestureRecognizer()}">
  <Label Text="Submit" />
</Frame>

<!-- ✅ Native Button (automatic: keyboard, focus, accessibility) -->
<Button Text="Submit"
        Command="{Binding SubmitCommand}" />

<!-- ❌ Custom checkbox (hard to manage) -->
<Grid ColumnDefinitions="44,*">
  <Frame BorderColor="Black" CornerRadius="4"
         GestureRecognizers="{new TapGestureRecognizer()}">
    <Label Text="✓" FontSize="20" />
  </Frame>
  <Label Text="I agree" Grid.Column="1" />
</Grid>

<!-- ✅ Native CheckBox (simple, accessible) -->
<CheckBox x:Name="AgreeCheckbox"
          IsChecked="{Binding IsAgreed}"
          AutomationId="agreeCheckbox" />
<Label Text="I agree to terms"
       Grid.Column="1" />

<!-- ✗ Non-semantic form structure -->
<VerticalStackLayout>
  <Label Text="Email" />
  <Entry Text="{Binding Email}" />
</VerticalStackLayout>

<!-- ✓ Semantic form with proper structure -->
<VerticalStackLayout Spacing="8">
  <Label Text="Email address"
         AutomationId="emailLabel" />
  <SfTextEntry x:Name="EmailEntry"
               Text="{Binding Email}"
               AutomationId="emailInput"
               Keyboard="Email"
               Required="True" />
</VerticalStackLayout>
```

**Use SemanticProperties for custom components:**

```xaml
<!-- ✓ GOOD - SemanticProperties for custom control -->
<Frame BorderColor="Blue"
       Padding="12"
       GestureRecognizers="{new TapGestureRecognizer {Command=Binding EditCommand}}">
  <Label Text="Edit Profile"
         AutomationId="editButton"
         SemanticProperties.Hint="Double-tap to edit your profile" />
</Frame>

<!-- ✓ GOOD - AutomationId for icon buttons -->
<ImageButton Source="menu_icon.png"
             AutomationId="menuButton"
             ToolTipProperties.Text="Open menu" />

<!-- ✓ GOOD - SemanticProperties for error messages -->
<Label Text="Email is required"
       TextColor="Red"
       AutomationId="emailError"
       SemanticProperties.Description="Validation error: Email field is required" />
```

**Validation Rules:**
- [ ] Buttons are `<Button>` or `<SfButton>` elements
- [ ] Checkboxes are `<CheckBox>` or custom with proper SemanticProperties
- [ ] Text inputs are `<Entry>` or `<SfTextEntry>`
- [ ] Inputs have associated `<Label>` elements with AutomationId
- [ ] No custom controls when Syncfusion/MAUI native control exists
- [ ] Icon buttons have AutomationId and ToolTipProperties.Text
- [ ] Custom components have proper SemanticProperties
- [ ] Error messages have AutomationId and SemanticProperties

---

## Cross-Platform Standards

### iOS, Android, Windows, macOS Compatibility

**MAUI automatically handles platform differences, but ensure consistency:**

```xaml
<!-- ✓ GOOD - Platform-specific layout adjustments -->
<ContentPage Padding="{OnPlatform iOS='0,40,0,0', Android='0,0,0,0', WinUI='0,0,0,0', MacCatalyst='0,0,0,0'}">
  <!-- Safe area handling for notches -->
</ContentPage>

<!-- ✓ GOOD - Platform-specific font sizes -->
<Label Text="Title"
       FontSize="{OnPlatform iOS=20, Android=18, WinUI=22, MacCatalyst=20}" />

<!-- ✓ GOOD - Platform-specific colors for theme -->
<Button TextColor="{AppThemeBinding Light={OnPlatform iOS=#000000, Android=#1F1F1F}, Dark={OnPlatform iOS=#FFFFFF, Android=#E8E8E8}}" />
```

**Validation Rules:**
- [ ] Safe areas handled for all platforms (iOS notch, Android system bars)
- [ ] Touch targets 44×44 dp minimum across all platforms
- [ ] Navigation patterns follow platform conventions (bottom tabs on iOS, side drawer on Android)
- [ ] AppBar/TitleBar appropriate for each platform
- [ ] Status bar styling platform-appropriate
- [ ] Fonts render correctly on all platforms
- [ ] Colors appear consistent in light and dark modes across platforms

---

## Testing Standards

**Automated accessibility validation:**
```
MAUI built-in tools:
- SemanticProperties validation (design-time checks)
- AutomationId consistency checking
- Focus order validation
- Contrast checking in ResourceDictionaries
```

**Manual testing—test on real devices:**
- [ ] **Keyboard navigation:** Tab through entire app on Windows/macOS
- [ ] **Screen reader (VoiceOver/TalkBack):** Listen on iOS/Android
- [ ] **Zoom:** Test at 200% size (no horizontal scroll)
- [ ] **Dark/Light modes:** AppTheme switching
- [ ] **All platforms:** iOS, Android, Windows, macOS
- [ ] **Multiple device sizes:** Phone, tablet, desktop

---

## Security Standards (.NET)

### 2.1 Input Validation

**Requirement:** Prevent SQL injection, XAML injection, and data tampering

**What to Check:**

```csharp
// ✗ BAD - Unsanitized user input in database query
string userInput = entry.Text;
string query = $"SELECT * FROM Users WHERE Name = '{userInput}'";
// Vulnerable to SQL injection!

// ✓ GOOD - Parameterized queries with user input
string userInput = entry.Text;
using (var connection = new SqlConnection(connectionString))
{
    var command = new SqlCommand("SELECT * FROM Users WHERE Name = @Name", connection);
    command.Parameters.AddWithValue("@Name", userInput);
    connection.Open();
    var result = command.ExecuteReader();
}

// ✗ BAD - Unvalidated URL navigation
await Shell.Current.GoToAsync(userInput);

// ✓ GOOD - Validate URL before navigation
if (Uri.TryCreate(userInput, UriKind.Absolute, out var uri) &&
    (uri.Scheme == "https" || uri.Scheme == "http"))
{
    await Launcher.Default.OpenAsync(uri);
}

// ✓ GOOD - Validate and sanitize text for display
public string SanitizeForDisplay(string input)
{
    // Use System.Net.WebUtility.HtmlEncode for XAML text
    return WebUtility.HtmlEncode(input);
}
```

**Validation Rules:**
- [ ] All database queries use parameterized queries
- [ ] No string interpolation in SQL queries
- [ ] User input validated before use
- [ ] URL validation before navigation
- [ ] No reflection with untrusted data
- [ ] Input length limits enforced
- [ ] Type validation on all inputs

---

### 2.2 Secrets & Environment Variables

**Requirement:** Never expose API keys, database credentials, or license keys in code

**What to Check:**

```csharp
// ✗ BAD - Hardcoded secrets in code
const string API_KEY = "sk_live_12345abcde";
const string DB_CONNECTION = "Server=prod;User Id=admin;Password=secret123";
const string LICENSE_KEY = "XXXX-XXXX-XXXX-XXXX";

// ✓ GOOD - Secrets from configuration
public class AppSettings
{
    public string ApiKey { get; set; }
    public string DatabaseConnection { get; set; }
    public string LicenseKey { get; set; }
}

// In appsettings.json (NOT committed to version control)
{
  "AppSettings": {
    "ApiKey": "sk_live_12345abcde",
    "DatabaseConnection": "Server=prod;User Id=admin;Password=secret123",
    "LicenseKey": "XXXX-XXXX-XXXX-XXXX"
  }
}

// Usage in MauiProgram.cs
var apiKey = builder.Configuration["AppSettings:ApiKey"];
builder.Services.AddSingleton(new ApiClient(apiKey));

// ✓ GOOD - Secure storage for user secrets on device
SecureStorage.SetAsync("user_token", secureToken);
var token = await SecureStorage.GetAsync("user_token");
```

**Validation Rules:**
- [ ] No hardcoded API keys in code
- [ ] No hardcoded database URLs/credentials
- [ ] No hardcoded license keys
- [ ] appsettings.json in .gitignore (for production secrets)
- [ ] User secrets configured for development
- [ ] SecureStorage used for device-side sensitive data
- [ ] Environment variables configured in deployment
- [ ] Secrets documented as required configuration

---

### 2.3 Dependency Security

**Requirement:** Use trustworthy, well-maintained NuGet packages

**What to Check:**

```xml
<!-- Project file (.csproj) -->
<ItemGroup>
  <PackageReference Include="Syncfusion.Maui.DataGrid" Version="25.1.41" />
  <PackageReference Include="Syncfusion.Maui.Buttons" Version="25.1.41" />
  <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.0" />
</ItemGroup>
```

**Validation Rules:**
- [ ] All packages from official NuGet.org registry
- [ ] Packages from reputable publishers (Syncfusion, Microsoft, community maintainers)
- [ ] Syncfusion packages only from official Syncfusion publisher
- [ ] Run `dotnet list package --outdated` regularly
- [ ] No abandoned/deprecated packages
- [ ] Pin major versions appropriately
- [ ] Review license compatibility (MIT, Apache 2.0, etc.)

---

## Performance Standards (MAUI)

### 3.1 Data Binding Optimization

**Requirement:** Prevent unnecessary UI updates and memory leaks

**What to Check:**

```csharp
// ✗ BAD - Binding to method (re-evaluates on every binding update)
<Label Text="{Binding GetCurrentTime()}" />

// ✓ GOOD - Bind to property that updates appropriately
public class MyViewModel : INotifyPropertyChanged
{
    private DateTime _currentTime;
    public DateTime CurrentTime
    {
        get => _currentTime;
        set
        {
            if (_currentTime != value)
            {
                _currentTime = value;
                OnPropertyChanged();
            }
        }
    }
}
<Label Text="{Binding CurrentTime}" />

// ✓ GOOD - Virtualization for large lists
<CollectionView ItemsSource="{Binding Items}"
                ItemsUpdatingScrollMode="KeepItemsInView"
                SelectionChangedCommand="{Binding SelectionChangedCommand}">
  <CollectionView.ItemTemplate>
    <DataTemplate>
      <StackLayout>
        <Label Text="{Binding Name}" />
      </StackLayout>
    </DataTemplate>
  </CollectionView.ItemTemplate>
</CollectionView>

// ✗ BAD - No event unsubscription (memory leak)
public MyPage()
{
    InitializeComponent();
    MessagingCenter.Subscribe<object>(this, "MyMessage", OnMessageReceived);
    // Never unsubscribed!
}

// ✓ GOOD - Unsubscribe in Dispose
protected override void OnDisappearing()
{
    base.OnDisappearing();
    MessagingCenter.Unsubscribe<object>(this, "MyMessage");
}
```

**Validation Rules:**
- [ ] Bindings are to properties, not methods
- [ ] CollectionView uses virtualization for 50+ items
- [ ] Event subscriptions are unsubscribed in OnDisappearing or Dispose
- [ ] No circular references in ViewModels
- [ ] Images are optimized (not full resolution on device)
- [ ] Async operations don't block UI thread

---

### 3.2 App Size & Responsiveness

**Requirement:** Keep app size reasonable and UI responsive

**Validation Rules:**
- [ ] App installation size < 100MB (target platform specific)
- [ ] No duplicate NuGet packages
- [ ] Images compressed and appropriately sized
- [ ] No heavy processing on main UI thread
- [ ] Async/await used for I/O and network operations
- [ ] Heavy operations moved to background threads (Task.Run)
- [ ] Lazy loading for heavy controls or data

---

## Code Quality Standards (C#)

### 4.1 C# Type Safety & Best Practices

**Requirement:** Full type safety with proper C# patterns

**What to Check:**

```csharp
// ✗ BAD - Untyped event handler
void OnButtonClicked(object sender, EventArgs e)
{
    var text = textEntry.Text;
    int.Parse(text);  // Potential crash if not numeric
}

// ✓ GOOD - Type-safe with validation
private void OnButtonClicked(object sender, EventArgs e)
{
    if (int.TryParse(textEntry.Text, out var value))
    {
        ProcessValue(value);
    }
    else
    {
        DisplayError("Please enter a valid number");
    }
}

// ✓ GOOD - Nullable reference types enabled
#nullable enable
public class UserViewModel
{
    public string? Email { get; set; }  // Explicitly nullable
    public string Name { get; set; } = string.Empty;  // Required, not null
    
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(Email) && 
               !string.IsNullOrEmpty(Name);
    }
}
```

**Validation Rules:**
- [ ] Nullable reference types enabled (`#nullable enable`)
- [ ] No `!` null-forgiving operators unless justified
- [ ] Event handlers properly typed
- [ ] Properties have clear return types
- [ ] Methods return meaningful types (not `void` when result needed)
- [ ] Use `TryParse` instead of `Parse` for safety
- [ ] XML documentation on public members (///)

---

### 4.2 Code Hygiene & Style

**Requirement:** Clean, maintainable C# code

**What to Check:**

```csharp
// ✗ BAD - var, Debug.WriteLine in production, unused code
public partial class MyPage : ContentPage
{
    private var name = "user";  // Type inference reduces clarity
    
    public void OnButtonClicked(object? sender, EventArgs e)
    {
        Debug.WriteLine($"Debug: {name}");  // Removed in production
        // var unused = 42;  // Commented out code
        var result = ProcessName(name);
    }
    
    private string ProcessName(string input) => input.ToUpper();
}

// ✓ GOOD - Explicit types, no Debug.WriteLine, clean code
public partial class MyPage : ContentPage
{
    private readonly string _name = "user";
    
    private readonly ILogger<MyPage> _logger;
    
    public MyPage()
    {
        InitializeComponent();
    }
    
    private void OnButtonClicked(object? sender, EventArgs e)
    {
        string result = ProcessName(_name);
        if (result != null)
        {
            DisplayMessage(result);
        }
    }
    
    /// <summary>
    /// Processes and converts a name to uppercase.
    /// </summary>
    /// <param name="input">The name to process</param>
    /// <returns>Uppercase name or empty string if input is null</returns>
    private string ProcessName(string input) => 
        string.IsNullOrEmpty(input) ? string.Empty : input.ToUpper();
}
```

**Validation Rules:**
- [ ] No `var` for ambiguous types (use explicit types for clarity)
- [ ] No `Debug.WriteLine` or `Console.WriteLine` in production
- [ ] No unused variables or `using` statements
- [ ] No commented-out code blocks (use version control)
- [ ] Consistent indentation (4 spaces, standard C#)
- [ ] Naming: PascalCase for classes/methods, camelCase for parameters
- [ ] XML documentation (`///`) on public members
- [ ] Use `readonly` for fields that don't change
- [ ] Use `?` for nullable reference types
- [ ] Remove dead code

---

## Validation Checklist

**WCAG 2.2 AA (Mobile) + MAUI Accessibility Checklist—run for every control:**

```
PERCEIVABLE (MAUI)
  ✓ All images have SemanticProperties.Description (or empty if decorative)
  ✓ Icon buttons have AutomationId and ToolTipProperties.Text
  ✓ Videos have captions and transcripts
  ✓ Color contrast ≥ 4.5:1 for text (or 3:1 for large)
  ✓ Color contrast ≥ 3:1 for UI components
  ✓ Color contrast ≥ 3:1 for focus indicators
  ✓ Information not conveyed by color alone (use text, icons, patterns)
  ✓ No auto-playing media
  ✓ Focus indicators have visible outline

OPERABLE (MAUI)
  ✓ All functionality accessible via keyboard (Tab navigation)
  ✓ Tab order logical (top-to-bottom, left-to-right)
  ✓ No focus traps (can Tab out of all controls)
  ✓ Focus indicators always visible (never hidden)
  ✓ Interactive targets ≥ 44×44 dp (touch-friendly)
  ✓ Adequate spacing between targets (≥ 8 dp)
  ✓ Drag/swipe gestures have button alternatives
  ✓ Escape closes modals/popups
  ✓ Enter/Space activates buttons
  ✓ Arrow keys work for CollectionView, TabView, etc.
  ✓ Focus managed when modal opens (first control focused)

UNDERSTANDABLE (MAUI)
  ✓ Default language set in MauiProgram.cs
  ✓ Semantic structure with proper heading levels (SemanticProperties)
  ✓ Every form field has associated Label
  ✓ Required fields marked ("*" or "(required)")
  ✓ Error messages clear and specific
  ✓ Error messages linked via AutomationId/SemanticDescription
  ✓ Form validation at appropriate times (field blur, form submit)
  ✓ Information not re-requested (use saved data when possible)
  ✓ Navigation consistent across app
  ✓ Help and support consistently placed
  ✓ Authentication offers multiple methods (not purely cognitive test)

ROBUST (MAUI)
  ✓ Native MAUI controls used (Button, SfTextEntry, CheckBox, etc.)
  ✓ Custom controls only when MAUI control doesn't exist
  ✓ Icon buttons have AutomationId and ToolTipProperties.Text
  ✓ Error fields have SemanticProperties describing the error
  ✓ Custom components have proper SemanticProperties
  ✓ All controls support platform accessibility (iOS VoiceOver, Android TalkBack)
  ✓ No obscured focus elements

SECURITY (.NET)
  ✓ All database queries use parameterized queries
  ✓ No string interpolation in SQL
  ✓ User input validated before use
  ✓ URL validation before navigation
  ✓ No reflection with untrusted data
  ✓ Input length limits enforced
  ✓ Type validation on all inputs
  ✓ No hardcoded API keys, database URLs, or secrets
  ✓ Secrets in configuration (appsettings.json), not code
  ✓ SecureStorage used for device-side sensitive data
  ✓ All NuGet packages from official NuGet.org
  ✓ License compatibility verified

PERFORMANCE (MAUI)
  ✓ Data bindings are to properties, not methods
  ✓ CollectionView uses virtualization for 50+ items
  ✓ Event subscriptions unsubscribed in OnDisappearing/Dispose
  ✓ No circular references in ViewModels
  ✓ Images optimized and appropriately sized
  ✓ Async/await used for I/O and network operations
  ✓ No heavy processing on UI thread
  ✓ App installation size < 100MB (target-specific)

CODE QUALITY (C#)
  ✓ Nullable reference types enabled (#nullable enable)
  ✓ No unvalidated null-forgiving operators (!)
  ✓ Event handlers properly typed
  ✓ Properties have clear return types
  ✓ Use TryParse instead of Parse
  ✓ XML documentation (///) on public members
  ✓ No Debug.WriteLine in production code
  ✓ No unused variables or using statements
  ✓ No commented-out code blocks
  ✓ Consistent naming: PascalCase (classes/methods), camelCase (params)
  ✓ Use readonly for non-changing fields
  ✓ Proper indentation (4 spaces)

CROSS-PLATFORM
  ✓ Safe areas handled for iOS, Android, Windows, macOS
  ✓ Touch targets work on all platforms
  ✓ Navigation patterns follow platform conventions
  ✓ Status bar styling platform-appropriate
  ✓ Fonts render correctly on all platforms
  ✓ Colors consistent in light/dark mode on all platforms
  ✓ Tested on real devices or emulators for each platform
```

---

## Auto-Fix Rules

**Stage 6 automatically fixes these issues:**

| Issue | Auto-Fix |
|-------|----------|
| Missing AutomationId on button | Add based on button text or icon context |
| Missing ToolTipProperties.Text on icon button | Add based on icon context |
| Missing SemanticProperties.Description on image | Add descriptive text (requires manual review) |
| Missing Label for form input | Add Label element with AutomationId linkage |
| Color contrast < 4.5:1 | Adjust TextColor or BackgroundColor in ResourceDictionary |
| Missing virtualization on CollectionView | Add ItemsUpdatingScrollMode="KeepItemsInView" for 50+ items |
| Data binding to method | Create property in ViewModel, bind to property |
| Event subscription without unsubscribe | Add unsubscribe in OnDisappearing |
| Hardcoded strings | Extract to ResourceDictionary or localization |
| Debug.WriteLine in production | Remove or conditionally compile |
| Unused using statements | Remove from top of file |
| Focus trap in control | Ensure Tab key navigates out |
| Non-native custom button | Replace with native Button control |
| Missing SemanticProperties on error message | Add HeadingLevel or Description |
| Unchecked null reference | Add null check or use TryParse |

---

**End of MAUI & .NET Standards Reference**  

**Version History:**
- **1.0.0** (May 5, 2026): Converted from Web Standards to MAUI & .NET Standards
- Aligned with WCAG 2.2 AA for mobile accessibility
- Added C# best practices and .NET security standards
- Integrated cross-platform (iOS, Android, Windows, macOS) considerations

**Related References:**
- `build.md` - MAUI build error troubleshooting
- `validation-rules.md` - Validation rules for Stage 6
- `stage-6-validation.md` - Validation stage workflow
