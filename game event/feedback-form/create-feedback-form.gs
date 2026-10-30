/**
 * Rift Jumpers — playtest feedback form generator.
 *
 * How to use (https://script.google.com → paste this file → save):
 *   - updateFeedbackForm: rewrites the questions of the EXISTING form (FORM_ID below).
 *     Same link, same QR code. Run this one whenever the questions change.
 *   - createFeedbackForm: builds a brand-new form (new link) — only for a fresh start.
 *
 * After running, open the Execution log: it prints the public link (for the QR code),
 * the edit link, and the responses spreadsheet.
 *
 * Question order matters: the "explain it / what is the Break for" questions come BEFORE
 * anything that names the Store/Gunsmith, so the form doesn't teach the answer it is testing.
 */

// The form at https://docs.google.com/forms/d/<FORM_ID>/edit
const FORM_ID = '10m7k3Q0jZXacTrYTVJDCT7HAKBs_5Ine0hqa_mmPhKQ';

function updateFeedbackForm() {
  const form = FormApp.openById(FORM_ID);

  // Replace every question. Old responses stay in the form, but their questions are gone,
  // so the responses go to a fresh spreadsheet with clean columns.
  form.getItems().forEach(item => form.deleteItem(item));
  buildQuestions(form);

  try { form.removeDestination(); } catch (e) { /* no spreadsheet linked yet */ }
  const sheet = SpreadsheetApp.create('Rift Jumpers — Playtest (responses)');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  publishAndLog(form, sheet);
}

function createFeedbackForm() {
  const form = FormApp.create('Rift Jumpers — Playtest');
  buildQuestions(form);

  const sheet = SpreadsheetApp.create('Rift Jumpers — Playtest (responses)');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  publishAndLog(form, sheet);
}

function publishAndLog(form, sheet) {
  // Newer Forms need an explicit publish before strangers can answer.
  try { form.setPublished(true); } catch (e) { /* older Forms: already accepting responses */ }
  form.setAcceptingResponses(true);

  let publicUrl = form.getPublishedUrl();
  try { publicUrl = form.shortenFormUrl(publicUrl); } catch (e) { /* keep the long link */ }

  Logger.log('PUBLIC LINK (QR code): ' + publicUrl);
  Logger.log('EDIT FORM:            ' + form.getEditUrl());
  Logger.log('RESPONSES SHEET:      ' + sheet.getUrl());
}

function buildQuestions(form) {
  form.setTitle('Rift Jumpers — Playtest');
  form
    .setDescription(
      'Thanks for playing! This takes about 3 minutes. There are no wrong answers — ' +
      'if something was confusing, that\'s on the game, not on you. 😉')
    .setProgressBar(true)
    .setCollectEmail(false)
    .setAllowResponseEdits(false)
    .setShowLinkToRespondAgain(false)
    .setConfirmationMessage('Thank you so much! 💖 If you want to tell us more, come talk to us at the table.');

  // --- 1. Your run ----------------------------------------------------------
  form.addSectionHeaderItem().setTitle('Your run');

  // Players rarely remember hero names after one run, so each choice names the hero's Skill
  // (weapons are shared by every hero, so they don't identify anyone).
  form.addCheckboxItem()
    .setTitle('Which hero did you play?')
    .setChoiceValues([
      'Max — the one who gets angry easily',
      'Pixie — the bunny girl who throws exploding bombs',
      'Brute — the big guy who charges through enemies',
      'Zara — the DJ who drops a speaker totem',
      'Yoru — the one with the flying hands',
      'Capy — the capybara who builds a turret',
    ])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('How many people played together?')
    .setChoiceValues(['1 (solo)', '2', '3', '4'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('How far did you get?')
    .setChoiceValues([
      'Went down before the first Break',
      'Reached 1 or 2 Breaks',
      'Reached 3 or more Breaks',
      'Reached the Boss',
      'Beat the Boss!',
    ])
    .setRequired(true);

  // Segments every other answer (newcomer vs genre veteran), so it lives on page 1.
  form.addMultipleChoiceItem()
    .setTitle('Have you played roguelites or "survivors" games before? (like Vampire Survivors, Brotato, Hades, Gunfire Reborn)')
    .setChoiceValues(['Yes, a lot', 'A little', 'Never'])
    .setRequired(true);

  // --- 2. Understanding the game (comprehension, unprompted first) ----------
  form.addPageBreakItem()
    .setTitle('Understanding the game')
    .setHelpText('Answer based on what you understood while playing — you don\'t need to get it "right".');

  form.addParagraphTextItem()
    .setTitle('In one or two sentences: how would you explain this game to a friend?')
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('Every so often the enemy waves stop and a "Break" starts. To you, the Break was…')
    .setChoiceValues([
      'A pause to catch my breath / wait',
      'Time to kill the enemies that were left',
      'Time to spend coins and get stronger',
      'I didn\'t notice there was a Break',
      'I don\'t know / didn\'t get it',
    ])
    .showOtherOption(true)
    .setRequired(true);

  // From here on the form names the Store/Gunsmith.
  form.addScaleItem()
    .setTitle('Was it clear that during the Break you can upgrade your character (Store and Gunsmith)?')
    .setBounds(1, 5)
    .setLabels('No idea', 'Super clear')
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('Did you use the Store or the Gunsmith? (check all that apply)')
    .setChoiceValues([
      'I used the Store',
      'I used the Gunsmith',
      'I couldn\'t find them',
      'I didn\'t have enough coins',
      'I didn\'t understand what they were for',
      'I ran out of time',
    ])
    .setRequired(true);

  form.addScaleItem()
    .setTitle('By the end of the run, did your character feel stronger?')
    .setBounds(1, 5)
    .setLabels('Same as the start', 'Way stronger')
    .setRequired(true);

  // Map POIs, named as the in-game ping feed names them.
  const POIS = [
    'Store',
    'Gunsmith',
    'Healing Shrine',
    'Cursed Rift',
    'Chests',
    'Platform Challenge (jumping puzzle)',
    'Team Challenge (Kill Rush, Flawless Hunt, Cursed Survival)',
  ];

  form.addSectionHeaderItem()
    .setTitle('Exploring the map');

  form.addCheckboxItem()
    .setTitle('Which of these did you find during your run? (check all that apply)')
    .setChoiceValues(POIS.concat(['None of these']))
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('Did you find a Challenge?')
    .setChoiceValues([
      'Yes, and we completed it',
      'Yes, but we failed it',
      'Found one, but didn\'t start it',
      'Didn\'t find any',
    ])
    .setRequired(true);

  // A rating grid instead of a strict 1..N ranking: players skip what they never found,
  // and the average score per row gives the ranking.
  form.addGridItem()
    .setTitle('How useful or fun was each one?')
    .setHelpText('Mark "Didn\'t use" for anything you didn\'t find or try.')
    .setRows(POIS)
    .setColumns(['Didn\'t use', 'Not worth it', 'OK', 'Useful / fun', 'Loved it']);

  // --- 3. Tell us everything ------------------------------------------------
  form.addPageBreakItem()
    .setTitle('Tell us everything')
    .setHelpText('The open questions matter most to us. Short is fine, honest is better.');

  form.addScaleItem()
    .setTitle('How much did you like the art style?')
    .setBounds(1, 5)
    .setLabels('Not for me', 'Love it')
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('How was the difficulty?')
    .setChoiceValues(['Too easy', 'Easy', 'Just right', 'Hard', 'Too hard'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('When you went down, it felt like…')
    .setChoiceValues([
      'My fault — I could have dodged it',
      'The game\'s fault — I didn\'t see what hit me',
      'I was too weak for the enemies',
      'I never went down',
    ])
    .showOtherOption(true);

  form.addParagraphTextItem()
    .setTitle('What confused you or left you lost? At what moment?');

  form.addParagraphTextItem()
    .setTitle('What was the best moment of your run?');

  form.addParagraphTextItem()
    .setTitle('What did you miss, or what would you change, in the game?');

  form.addParagraphTextItem()
    .setTitle('Free space: anything else you want to tell us')
    .setHelpText('Bugs, ideas, praise, criticism, hero suggestions… anything goes.');

  form.addMultipleChoiceItem()
    .setTitle('When the run ended, did you want to play again?')
    .setChoiceValues(['Yes, right away', 'Maybe another day', 'No'])
    .setRequired(true);

  form.addMultipleChoiceItem()
    .setTitle('If this game were on Steam, what is the most you would pay for it at full price (no sale)?')
    .setChoiceValues([
      'I\'d only play it if it were free',
      'US$ 0.99',
      'US$ 4.99',
      'US$ 9.99',
      'US$ 14.99',
      'US$ 19.99',
    ])
    .setRequired(true);

  form.addTextItem()
    .setTitle('Want to hear about the next playtest? Leave your email or @ (optional)')
    .setHelpText('We only use it to tell you about Rift Jumpers. No spam.');
}
