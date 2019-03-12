import { async, ComponentFixture, TestBed } from "@angular/core/testing";

import { SideBarChannelComponent } from "./side-bar-channel.component";

describe("SideBarChannelComponent", () => {
  let component: SideBarChannelComponent;
  let fixture: ComponentFixture<SideBarChannelComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ SideBarChannelComponent ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SideBarChannelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });
});
