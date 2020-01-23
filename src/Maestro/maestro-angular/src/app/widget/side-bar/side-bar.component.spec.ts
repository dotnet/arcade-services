import { async, ComponentFixture, TestBed } from "@angular/core/testing";
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of } from 'rxjs';

import { SideBarComponent } from "./side-bar.component";
import { ChannelService } from 'src/app/services/channel.service';
import { StatefulModule } from 'src/stateful';

describe("SideBarComponent", () => {
  let component: SideBarComponent;
  let fixture: ComponentFixture<SideBarComponent>;

  beforeEach(async(() => {
    const mockChannelService = jasmine.createSpyObj("ChannelService", ["getChannels"]);
    TestBed.configureTestingModule({
      declarations: [
        SideBarComponent,
      ],
      providers: [
        {
          provide: ChannelService,
          useValue: mockChannelService,
        },
      ],
      imports: [
        StatefulModule,
      ],
      schemas: [
        NO_ERRORS_SCHEMA,
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SideBarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });
});
