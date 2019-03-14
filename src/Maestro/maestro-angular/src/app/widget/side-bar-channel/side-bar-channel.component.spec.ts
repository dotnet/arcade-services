import { async, ComponentFixture, TestBed } from "@angular/core/testing";
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';

import { SideBarChannelComponent } from "./side-bar-channel.component";
import { MaestroService } from 'src/maestro-client';

describe("SideBarChannelComponent", () => {
  let component: SideBarChannelComponent;
  let fixture: ComponentFixture<SideBarChannelComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        SideBarChannelComponent,
      ],
      providers: [
        {
          provide: MaestroService,
          useValue: {
            defaultChannels: {
              listAsync() {
                return of();
              },
            },
          },
        },
      ],
      imports: [
        RouterTestingModule,
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
