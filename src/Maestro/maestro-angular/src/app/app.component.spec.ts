import { async, TestBed } from "@angular/core/testing";
import { RouterTestingModule } from "@angular/router/testing";

import { AppComponent } from "./app.component";
import { NgbCollapseModule } from '@ng-bootstrap/ng-bootstrap';
import { UriEncodePipe } from './uri-encode.pipe';
import { SideBarComponent } from './widget/side-bar/side-bar.component';
import { SideBarChannelComponent } from './widget/side-bar-channel/side-bar-channel.component';
import { NO_ERRORS_SCHEMA } from '@angular/core';

describe("AppComponent", () => {
  beforeEach(async(() => {
    TestBed.configureTestingModule({
      imports: [
        RouterTestingModule,
      ],
      declarations: [
        AppComponent,
        UriEncodePipe,
      ],
      schemas: [
        NO_ERRORS_SCHEMA, // Allow unrecognized elements (other components we don't want to test)
      ],
    }).compileComponents();
  }));

  beforeEach(() => {
    (window as any).applicationData = {
      brand: "Brand",
      userName: "Nobody",
    };
  })

  it("should create the app", () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.debugElement.componentInstance;
    expect(app).toBeTruthy();
  });
});
