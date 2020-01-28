import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';

import { StatefulModule } from 'src/stateful';
import { ChannelComponent } from './channel.component';

import { MaestroService } from 'src/maestro-client';
import { ChannelService} from 'src/app/services/channel.service';

describe('ChannelComponent', () => {
  let component: ChannelComponent;
  let fixture: ComponentFixture<ChannelComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ ChannelComponent ],
      providers: [
        {
          provide: MaestroService,
          useValue: {
          },
        },
        {
          provide: ChannelService,
          useValue: {
          },
        }
      ],
      imports: [
        RouterTestingModule,
        StatefulModule,
      ],
      schemas: [NO_ERRORS_SCHEMA],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(ChannelComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
