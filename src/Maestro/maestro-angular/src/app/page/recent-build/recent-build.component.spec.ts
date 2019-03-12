import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { of } from 'rxjs';
import { RouterTestingModule } from '@angular/router/testing';

import { RecentBuildComponent } from './recent-build.component';
import { StatefulModule } from 'src/stateful';
import { MaestroService } from 'src/maestro-client';
import { ChannelService } from 'src/app/services/channel.service';

describe('RecentBuildComponent', () => {
  let component: RecentBuildComponent;
  let fixture: ComponentFixture<RecentBuildComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ RecentBuildComponent ],
      providers: [
        {
          provide: ChannelService,
          useValue: {
            getChannels() {
              return of();
            }
          }
        },
        {
          provide: MaestroService,
          useValue: {

          },
        },
      ],
      imports: [
        RouterTestingModule,
        StatefulModule,
      ],
      schemas: [ NO_ERRORS_SCHEMA ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(RecentBuildComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
