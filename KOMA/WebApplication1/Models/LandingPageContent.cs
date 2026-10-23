using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer B. Single-row table with the editable text and photo of the customer landing page
    // (Pages/Index.cshtml). Edited from Admin > Website (/Admin/LandingPage). If the row does not exist yet,
    // the landing page uses these defaults - they match the original hardcoded wording.
    // In the text boxes, a new line becomes a line break on the page. Optional fields left empty are hidden.
    public class LandingPageContent
    {
        public int Id { get; set; }

        // ---- Hero (top banner) ----
        [Display(Name = "Small heading"), StringLength(60)]
        public string? HeroEyebrow { get; set; } = "A LITTLE TASTE OF JAPAN";

        [Display(Name = "Main heading"), Required, StringLength(80)]
        public string HeroTitle { get; set; } = "Good food.";

        [Display(Name = "Main heading - second line (italic)"), StringLength(80)]
        public string? HeroTitleAccent { get; set; } = "Better together.";

        [Display(Name = "Introduction"), StringLength(300)]
        public string? HeroIntro { get; set; } = "Bring your appetite. Bring your favourite people.\nThere is always a little more to enjoy at KOMA.";

        [Display(Name = "Button text"), Required, StringLength(40)]
        public string HeroButtonText { get; set; } = "Find your favourite";

        // Uploaded banner photo; null = the original photo (wwwroot/images/koma/japanese-table.jpg).
        public string? HeroImageUrl { get; set; }

        // ---- Dishes ----
        [Display(Name = "Small heading"), StringLength(60)]
        public string? FoodEyebrow { get; set; } = "SOMETHING GOOD IS ON THE TABLE";

        [Display(Name = "Heading"), Required, StringLength(80)]
        public string FoodTitle { get; set; } = "What are you craving?";

        [Display(Name = "Introduction"), StringLength(300)]
        public string? FoodIntro { get; set; } = "A familiar favourite or something a little different.\nTake a look around. Find your next good bite.";

        [Display(Name = "Show the Recommended row")]
        public bool ShowRecommended { get; set; } = true;

        [Display(Name = "Recommended row title"), Required, StringLength(40)]
        public string RecommendedTitle { get; set; } = "Recommended";

        [Display(Name = "Show the Seasonal row")]
        public bool ShowSeasonal { get; set; } = true;

        [Display(Name = "Seasonal row title"), Required, StringLength(40)]
        public string SeasonalTitle { get; set; } = "Seasonal Specials";

        // ---- Story ----
        [Display(Name = "Show this section")]
        public bool ShowStory { get; set; } = true;

        [Display(Name = "Small heading"), StringLength(60)]
        public string? StoryEyebrow { get; set; } = "THE KOMA WAY";

        [Display(Name = "Heading"), Required, StringLength(80)]
        public string StoryTitle { get; set; } = "Good food brings us together.";

        [Display(Name = "Text"), StringLength(600)]
        public string? StoryText { get; set; } = "We believe the best meals are as much about the people as the food. Bring your favourite company, settle in, and enjoy a meal at your own pace.";

        // ---- Experience (three cards) ----
        [Display(Name = "Show this section")]
        public bool ShowExperience { get; set; } = true;

        [Display(Name = "Small heading"), StringLength(60)]
        public string? ExperienceEyebrow { get; set; } = "MAKE YOURSELF AT HOME";

        [Display(Name = "Heading"), Required, StringLength(80)]
        public string ExperienceTitle { get; set; } = "More than a meal.";

        [Display(Name = "Card 1 label"), StringLength(40)] public string? Card1Eyebrow { get; set; } = "01 / THE FOOD";
        [Display(Name = "Card 1 heading"), StringLength(80)] public string? Card1Title { get; set; } = "Find your next favourite.";
        [Display(Name = "Card 1 text"), StringLength(300)] public string? Card1Text { get; set; } = "Explore Japanese-inspired flavours, from delicate bites to crisp, satisfying crunches.";

        [Display(Name = "Card 2 label"), StringLength(40)] public string? Card2Eyebrow { get; set; } = "02 / THE COMPANY";
        [Display(Name = "Card 2 heading"), StringLength(80)] public string? Card2Title { get; set; } = "There’s room for everyone.";
        [Display(Name = "Card 2 text"), StringLength(300)] public string? Card2Text { get; set; } = "Bring friends, gather family, or enjoy a quiet meal on your own.";

        [Display(Name = "Card 3 label"), StringLength(40)] public string? Card3Eyebrow { get; set; } = "03 / THE MOMENT";
        [Display(Name = "Card 3 heading"), StringLength(80)] public string? Card3Title { get; set; } = "Stay a little longer.";
        [Display(Name = "Card 3 text"), StringLength(300)] public string? Card3Text { get; set; } = "Catch up over a meal, celebrate an occasion, or simply enjoy being here.";

        // ---- Closing invitation ----
        [Display(Name = "Closing heading"), Required, StringLength(120)]
        public string InvitationHeading { get; set; } = KomaBrand.InvitationHeading;

        [Display(Name = "Last updated")]
        public DateTime? UpdatedAt { get; set; }
    }
}
