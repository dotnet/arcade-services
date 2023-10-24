import { Component, ViewEncapsulation, OnInit } from "@angular/core";
import { NgbDropdown } from '@ng-bootstrap/ng-bootstrap';
import { CookieService } from './services/cookie.service';

interface Theme {
  name: string;
  file: string;
}

@Component({
  selector: "mc-root",
  templateUrl: "./app.component.html",
  styleUrls: ["./app.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class AppComponent implements OnInit {
  static themeCookieName = "Maestro.Theme";

  constructor(private cookieService: CookieService){}

  public title = ".NET Mission Control";
  public navbarOpen = false;
  public sidebarOpen = false;
  public returnUrl = location.pathname + location.search;
  public brand: string = (window as any).applicationData.brand;
  public userName: string = (window as any).applicationData.userName;
  public authorized: boolean = (window as any).applicationData.authorized;

  public currentTheme!: string;
  public themes: Theme[] = (window as any).applicationData.themes;

  public isApiRedirecting: boolean = (window as any).applicationData.isApiRedirecting;

  public get isApiRedirectSkipped(): boolean {
    if (this.cookieService.get("Skip-Api-Redirect")) {
      return true;
    }
    return false;
  }

  public set isApiRedirectSkipped(value: boolean) {
    if (value === this.isApiRedirectSkipped) {
      return;
    }
    if (value) {
      this.cookieService.set("Skip-Api-Redirect", "1");
      window.location.reload();
    } else {
      this.cookieService.remove("Skip-Api-Redirect");
      window.location.reload();
    }
  }

  public ngOnInit(): void {
    this.currentTheme = this.cookieService.get(AppComponent.themeCookieName) || "light";
  }

  public selectTheme(name: string) {
    if (this.currentTheme === name) {
      return;
    }

    this.currentTheme = name;
    this.cookieService.set(AppComponent.themeCookieName, name);

    const existingThemeStyleElement = document.querySelector("head link[purpose='theme']");
    if (!existingThemeStyleElement) {
      return;
    }

    const selectedThemeStyles = this.themes.find(t => t.name === name);
    if (selectedThemeStyles) {
      existingThemeStyleElement.setAttribute("href", selectedThemeStyles.file);
    }
  }
}
