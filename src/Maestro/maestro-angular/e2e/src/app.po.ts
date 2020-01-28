import { browser, by, element, ExpectedConditions as until } from "protractor";

export class AppPage {
  public navigateTo() {
    // our app uses timers that never stop firing. Waiting for angular timesout because of these.
    // test methods should wait for the presence of elements
    browser.waitForAngularEnabled(false);
    return browser.get(browser.baseUrl) as Promise<any>;
  }

  public async getBrandText() {
    await browser.wait(until.presenceOf(element(by.css("mc-root .navbar-brand"))), 5000);
    return await element(by.css("mc-root .navbar-brand")).getText();
  }
}
