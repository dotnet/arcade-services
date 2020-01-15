import { async, ComponentFixture, TestBed } from "@angular/core/testing";
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';

import { SideBarChannelComponent } from "./side-bar-channel.component";
import { MockRepoName } from "src/app/mock-pipes.spec";
import { StatefulModule } from 'src/stateful';
import { ChannelService } from 'src/app/services/channel.service';

describe("SideBarChannelComponent", () => {
  let component: SideBarChannelComponent;
  let fixture: ComponentFixture<SideBarChannelComponent>;

  beforeEach(async(() => {
    const mockChannelService = jasmine.createSpyObj("ChannelService", ["getRepositories"]);
    TestBed.configureTestingModule({
      declarations: [
        SideBarChannelComponent,
        MockRepoName,
      ],
      providers: [
        {
          provide: ChannelService,
          useValue: mockChannelService,
        },
      ],
      imports: [
        RouterTestingModule,
        StatefulModule,
      ],
      schemas: [
        NO_ERRORS_SCHEMA,
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SideBarChannelComponent);
    component = fixture.componentInstance;
    component.channel = { id: 1 } as any;
    fixture.detectChanges();
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });
});
