import { Component, ViewEncapsulation } from "@angular/core";

@Component({
  selector: "mc-root",
  templateUrl: "./app.component.html",
  styleUrls: ["./app.component.scss"],
  encapsulation: ViewEncapsulation.None,
})
export class AppComponent {
  public title = ".NET Mission Control";
  public navbarOpen = false;
  public sidebarOpen = false;
  public returnUrl = location.pathname + location.search;
  public brand = (window as any).applicationData.brand;
  public userName = (window as any).applicationData.userName;
}
